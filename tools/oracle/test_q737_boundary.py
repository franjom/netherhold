"""Synthetic boundary-selection tests; no DK2 process or proprietary data."""
import struct
import json
from pathlib import Path
import tempfile
import unittest
from types import SimpleNamespace

try:
    import q737_boundary as capture
except ModuleNotFoundError:
    capture = None


class Memory:
    def __init__(self):
        self.data = {}
        self.actor, self.definition, self.container, self.boulder = 0x10000, 0x20000, 0x30000, 0x40000
        self.put(self.actor, bytes(0x53F))
        self.put(self.definition, bytes(0x800))
        self.value(0x006F2A08+4, self.container)
        self.value(self.container+4, 12)
        self.value(0x006F2A08+0x15A0, 123)
        for offset,value,fmt in [(4,453,'<H'),(0x28,3,'<H'),(0x36F,10,'<B'),(0x403,1,'<B'),
            (0x370,self.definition,'<I'),(0x25E,256,'<I'),(0x40,500,'<i'),(0x266,11,'<I'),
            (0x526,1752,'<H'),(0x524,1752,'<H'),(0x533,1155,'<H'),(0x3EA,0x40,'<I')]:
            self.value(self.actor+offset,value,fmt)
        self.value(self.definition+0x74C,8)
        self.value(self.definition+0x696,1000,'<H')
        self.value(0x006ED540+1155,1,'<B')
        self.value(0x006EE540+1155*4,self.boulder)
        self.put(self.boulder,bytes(0x189))
        self.value(self.boulder+4,1155,'<H')
        self.value(self.boulder+0x12,2)
        self.value(self.boulder+0x12C,74,'<B')

    def put(self,address,data):
        self.data.update((address+i,value) for i,value in enumerate(data))

    def value(self,address,value,fmt='<I'):
        self.put(address,struct.pack(fmt,value))

    def read(self,address,size):
        try:
            return bytes(self.data[address+i] for i in range(size))
        except KeyError as error:
            raise OSError('unreadable synthetic memory') from error


class BoundaryTests(unittest.TestCase):
    def module(self):
        self.assertIsNotNone(capture,'Q737 boundary adapter has not been implemented')
        return capture

    def test_loaded_interval_and_entry_stamp_are_read_from_the_live_definition(self):
        m = Memory()
        state = self.module().subject_state(m.read,m.actor)
        self.assertEqual((256,500,8,1),(state['state'],state['health'],state['stunInterval'],state['elapsed']))
        self.assertTrue(state['holdWouldContinue'])
        self.assertEqual(74,state['threat']['objectType'])

    def test_first_selection_requires_boulder_stun_but_following_dying_updates_remain_selected(self):
        m = Memory()
        window = self.module().TrollWindow()
        m.value(m.actor+0x25E,255)
        self.assertFalse(window.select(m.actor,m.read))
        m.value(m.actor+0x25E,256)
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.actor+0x25E,6)
        m.value(m.actor+0x32D,221)
        self.assertEqual(6,window.sample('after',m.read)['state'])
        m.value(m.container+4,13)
        m.value(m.actor+0x25E,216)
        m.value(m.actor+0x40,240,'<i')
        self.assertTrue(window.select(m.actor,m.read))
        self.assertEqual(216,window.sample('before',m.read)['state'])
        self.assertFalse(window.select(m.actor+0x1000,m.read))

    def test_pair_clock_drift_is_rejected(self):
        m = Memory()
        window = self.module().TrollWindow()
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.container+4,13)
        with self.assertRaises(ValueError):
            window.sample('after',m.read)

    def test_identity_drift_is_rejected(self):
        m = Memory()
        window = self.module().TrollWindow()
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.actor+4,454,'<H')
        with self.assertRaises(ValueError):
            window.sample('after',m.read)

    def test_unreadable_loaded_interval_fails_closed(self):
        m = Memory()
        del m.data[m.definition+0x74C]
        with self.assertRaises(OSError):
            self.module().subject_state(m.read,m.actor)
    def test_capture_error_survives_cleanup_error_and_both_are_recorded(self):
        process = SimpleNamespace(pid=42,poll=lambda: None,
            terminate=lambda: (_ for _ in ()).throw(OSError('cleanup failed')))
        debugger = SimpleNamespace(process=process,completed=0)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(ValueError,'capture failed'):
                self.module().run_owned(debugger,
                    lambda: (_ for _ in ()).throw(ValueError('capture failed')),lambda: None,output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('capture failed',receipt['error'])
            self.assertIn('cleanup failed',receipt['cleanupError'])
            self.assertFalse(receipt['ownedClosed'])
    def test_validation_failure_is_recorded_after_owned_child_shutdown(self):
        closed = []
        process = SimpleNamespace(pid=42,poll=lambda: 0 if closed else None,
            terminate=lambda: closed.append(True),wait=lambda timeout: 0)
        debugger = SimpleNamespace(process=process,completed=4)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(ValueError,'level changed'):
                self.module().run_owned(debugger,lambda: None,
                    lambda: (_ for _ in ()).throw(ValueError('level changed')),output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('level changed',receipt['error'])
            self.assertTrue(receipt['ownedClosed'])
    def test_cleanup_error_prevents_success_and_has_a_receipt(self):
        process = SimpleNamespace(pid=42,poll=lambda: None,
            terminate=lambda: (_ for _ in ()).throw(OSError('cleanup failed')))
        debugger = SimpleNamespace(process=process,completed=4)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(OSError,'cleanup failed'):
                self.module().run_owned(debugger,lambda: None,lambda: None,output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('cleanup failed',receipt['cleanupError'])
            self.assertFalse(receipt['ownedClosed'])


if __name__=='__main__':
    unittest.main()
