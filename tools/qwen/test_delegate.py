import importlib.util
import pathlib
import io
import json
import tempfile
from concurrent.futures import ThreadPoolExecutor
import urllib.error
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('delegate', pathlib.Path(__file__).with_name('delegate.py'))
d = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(d)


class Contracts(unittest.TestCase):
    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(prefix='qwen-contract-')
        self.addCleanup(self.fixture.cleanup)
        self.root = pathlib.Path(self.fixture.name)
        self.root_patch = patch.object(d, 'ROOT', self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)
        self.env = patch.dict(d.os.environ, {'QWEN_API_BASE_URL': 'http://example.test/v1', 'QWEN_API_KEY': 'secret', 'QWEN_MODEL_ID': 'actual-id'})
        self.env.start()
        self.addCleanup(self.env.stop)

    def test_validation_environment_has_no_credentials(self):
        with patch.dict(d.os.environ, {'GH_TOKEN': 'hidden', 'QWEN_WORKER_ACTIVE': ''}):
            env = d.validation_environment()
        self.assertNotIn('GH_TOKEN', env)
        self.assertNotIn('QWEN_API_KEY', env)
        self.assertEqual(env['QWEN_WORKER_ACTIVE'], '1')

    def test_escaped_secret_redacted_before_serialization(self):
        with patch.dict(d.os.environ, {'QWEN_API_KEY': 'a"b\\c'}):
            target = self.root / 'secret.json'
            d.save(target, {'nested': ['a"b\\c']})
            self.assertEqual(json.loads(target.read_text())['nested'], ['[REDACTED]'])

    def test_missing_config(self):
        with patch.dict(d.os.environ, {}, clear=True), self.assertRaises(ValueError):
            d.configuration()

    def test_scope_traversal(self):
        with self.assertRaises(ValueError):
            d.validate_task(dict(id='x', description='x', allowed_paths=['../escape'], acceptance_criteria=['x']))

    def test_retry_limit(self):
        with self.assertRaises(ValueError):
            d.validate_task(dict(id='x', description='x', allowed_paths=['src'], acceptance_criteria=['x'], attempt=4))

    def test_scope(self):
        self.assertTrue(d.in_scope('src/a.cs', ['src']))
        self.assertFalse(d.in_scope('src-other/a.cs', ['src']))

    def test_invalid_summary(self):
        with self.assertRaises(ValueError):
            d.validate_summary({'status': 'complete'})

    def test_redaction(self):
        self.assertEqual(d.redact('hello secret', 'secret'), 'hello [REDACTED]')

    def test_invalid_credentials(self):
        with patch.object(d.urllib.request, 'urlopen', side_effect=urllib.error.HTTPError('url', 401, 'unauthorized', {}, None)):
            with self.assertRaisesRegex(ValueError, '401'):
                d.request_api('/models')

    def test_unreachable(self):
        with patch.object(d.urllib.request, 'urlopen', side_effect=urllib.error.URLError('offline')):
            with self.assertRaisesRegex(ValueError, 'unreachable'):
                d.request_api('/models')

    def test_invalid_model(self):
        with patch.object(d, 'request_api', return_value=io.StringIO('{"data": []}')):
            with self.assertRaisesRegex(ValueError, 'absent'):
                d.validate_api()

    def test_actual_process_timeout(self):
        with self.assertRaises(TimeoutError):
            d.execute([d.sys.executable, '-c', 'import time; time.sleep(10)'], d.ROOT, 0.1)

    def test_process_failure(self):
        code, log = d.execute([d.sys.executable, '-c', 'raise SystemExit(7)'], d.ROOT, 5)
        self.assertEqual(code, 7)

    def test_permissions(self):
        config = d.overlay({'allowed_paths': ['src'], 'mode': 'review'}, pathlib.Path('work'), pathlib.Path('report.json'))
        self.assertEqual(config['permission']['bash'], 'deny')
        self.assertEqual(config['permission']['edit']['*'], 'deny')
        self.assertNotIn('secret', json.dumps(config))

    def test_limits_require_pair(self):
        with patch.dict(d.os.environ, {'QWEN_CONTEXT_TOKENS': '8192', 'QWEN_OUTPUT_TOKENS': ''}):
            with self.assertRaisesRegex(ValueError, 'both'):
                d.overlay({'allowed_paths': ['src']}, pathlib.Path('work'), pathlib.Path('report.json'))

    def test_overlay_contract(self):
        config = d.overlay({'allowed_paths': ['src']}, self.root, self.root / 'summary.json')
        self.assertEqual(config['provider']['qwen']['npm'], '@ai-sdk/openai-compatible')
        self.assertEqual(config['permission']['external_directory'], 'deny')
        self.assertEqual(config['permission']['task'], 'deny')
        self.assertEqual(config['permission']['edit']['src/*'], 'allow')
        self.assertEqual(config['provider']['qwen']['options']['apiKey'], '{env:QWEN_API_KEY}')

    def test_recursion_denied_before_executable(self):
        with patch.dict(d.os.environ, {'QWEN_WORKER_ACTIVE': '1'}), patch.object(d, 'executable') as executable:
            with self.assertRaisesRegex(ValueError, 'Recursive'):
                d.run_task({}, self.root / 'never')
            executable.assert_not_called()

    def test_retry_requires_result_path(self):
        with self.assertRaisesRegex(ValueError, 'previous_result'):
            d.validate_task(dict(id='retry', description='fix', allowed_paths=['src'], acceptance_criteria=['a'], attempt=2))

    def test_response_read_timeout(self):
        from unittest.mock import Mock
        response = Mock()
        response.read.side_effect = TimeoutError('slow')
        with self.assertRaisesRegex(ValueError, 'read failed or timed out'):
            d.read_response(response)

    def test_git_error_retains_stderr(self):
        with self.assertRaisesRegex(ValueError, 'not a git repository'):
            d.git('status', cwd=self.root)

    def git_fixture(self):
        d.git('init', cwd=self.root)
        (self.root / '.gitignore').write_text('bin/\nobj/\n')
        (self.root / 'source file.txt').write_text('baseline')
        d.git('add', '.', cwd=self.root)
        d.git('-c', 'user.name=Qwen Test', '-c', 'user.email=qwen-test@example.invalid', 'commit', '-m', 'baseline', cwd=self.root)

    def actual_run(self, behavior, task=None):
        task = task or dict(id='actual', description='task' * 15000, allowed_paths=['source file.txt', 'new file.txt'], acceptance_criteria=['done'], validation_commands=[['validator']])
        def worker(argv, cwd, timeout, env=None):
            if argv[0] == 'validator':
                self.assertNotIn('QWEN_API_KEY', env)
                self.assertNotIn('GH_TOKEN', env)
                self.assertEqual(env['QWEN_WORKER_ACTIVE'], '1')
                (cwd / 'bin').mkdir(exist_ok=True)
                (cwd / 'bin/result.dll').write_bytes(b'ignored binary')
                if behavior == 'validation_edit':
                    (cwd / 'source file.txt').write_text('validator changed source')
                return 0, 'pass'
            self.assertLess(sum(map(len, argv)), 2000)
            self.assertIn('--file', argv)
            prompt = pathlib.Path(argv[argv.index('--file') + 1]).read_text()
            self.assertIn(task['description'], prompt)
            summary = cwd / ('.qwen-summary-' + cwd.name[5:] + '.json')
            summary.write_text(json.dumps(dict(status='completed', changes=[], tests=[], unresolved_issues=[])))
            (cwd / 'source file.txt').write_text('worker changed source')
            (cwd / 'new file.txt').write_text('new source')
            return 0, '{}'
        with patch.object(d, 'executable', return_value='fake'), patch.object(d, 'execute', side_effect=worker), patch.dict(d.os.environ, {'GH_TOKEN': 'must-not-inherit'}):
            return d.run_task(task, self.root / ('output-' + d.uuid.uuid4().hex))

    def test_ignored_build_outputs_and_new_file_diff(self):
        self.git_fixture()
        result = self.actual_run('success')
        self.assertEqual(result['status'], 'awaiting_review', result)
        self.assertIn('bin/result.dll', result['ignored_artifacts'])
        self.assertEqual(result['worker_files_modified'], ['new file.txt', 'source file.txt'])
        diff = pathlib.Path(result['diff_location']).read_text()
        self.assertIn('new source', diff)
        self.assertNotIn('ignored binary', diff)

    def test_validation_source_edit_rejected_even_in_scope(self):
        self.git_fixture()
        result = self.actual_run('validation_edit')
        self.assertEqual(result['status'], 'failed')
        self.assertTrue(any('Validation modified source' in x for x in result['errors']))

    def test_review_mode_edits_rejected(self):
        self.git_fixture()
        task = dict(id='review', description='review', allowed_paths=[], acceptance_criteria=['report'], mode='review')
        result = self.actual_run('success', task)
        self.assertEqual(result['status'], 'failed')
        self.assertTrue(any('Unauthorized' in x for x in result['errors']))

    def test_valid_revise_retry(self):
        self.git_fixture()
        prior = self.root / 'prior.json'
        prior.write_text(json.dumps(dict(task_id='retry', attempt=1, supervisor_decision='revise')))
        task = dict(id='retry', description='bounded revision', allowed_paths=['source file.txt', 'new file.txt'], acceptance_criteria=['done'], attempt=2, previous_result=str(prior))
        result = self.actual_run('success', task)
        self.assertEqual(result['status'], 'awaiting_review', result)

    def test_status_and_inventory_preserve_spaced_names(self):
        self.git_fixture()
        (self.root / 'source file.txt').rename(self.root / 'renamed file.txt')
        d.git('add', '.', cwd=self.root)
        status = d.git('status', '--porcelain=v1', '-z', cwd=self.root)
        self.assertIn('renamed file.txt\x00source file.txt\x00', status)
        self.assertIn('renamed file.txt', d.inventory(self.root))

    def test_inventory_hashes_only_changed_paths_and_marks_deletion(self):
        self.git_fixture()
        with patch.object(d.hashlib, 'sha256', wraps=d.hashlib.sha256) as digest:
            self.assertEqual(d.inventory(self.root), {})
            digest.assert_not_called()
        (self.root / 'source file.txt').unlink()
        self.assertEqual(d.inventory(self.root), {'source file.txt': '<deleted>'})

    def simulated_run(self, root, name, behavior='success', validation=False):
        task = dict(id=name, description='bounded test', allowed_paths=['src'], acceptance_criteria=['a'])
        if validation:
            task['validation_commands'] = [['fake-test']]
        def fake_git(*args, cwd=None):
            if args[0] == 'worktree':
                pathlib.Path(args[4]).mkdir(parents=True)
            if args[0] == 'status':
                return '\0'.join('?? ' + p.relative_to(cwd).as_posix() for p in cwd.rglob('*') if p.is_file()) + '\0'
            return ''
        def worker(argv, cwd, timeout, env=None):
            if argv[0] == 'fake-test':
                return 1, 'failed assertion'
            if behavior == 'timeout':
                raise TimeoutError('worker timed out')
            if behavior == 'failure':
                return 2, 'failure'
            summary = next(iter([cwd / ('.qwen-summary-' + cwd.name[5:] + '.json')]))
            summary.write_text('{}' if behavior == 'invalid' else json.dumps(dict(status='completed', changes=[], tests=[], unresolved_issues=[])))
            if behavior == 'unauthorized':
                (cwd / 'outside.txt').write_text('unauthorized')
            return 0, '{}'
        with patch.object(d, 'ROOT', root), patch.object(d, 'executable', return_value='fake'), patch.object(d, 'git', side_effect=fake_git), patch.object(d, 'execute', side_effect=worker):
            return d.run_task(task, root / name)

    def test_worker_lifecycle_failures_and_preservation(self):
        root = self.root
        for behavior in ('timeout', 'failure', 'invalid', 'unauthorized'):
            result = self.simulated_run(root, behavior, behavior)
            self.assertEqual(result['status'], 'failed', behavior)
            self.assertTrue(pathlib.Path(result['worktree']).exists())
            self.assertTrue(result['errors'])

    def test_validation_failure(self):
        root = self.root
        result = self.simulated_run(root, 'validation', validation=True)
        self.assertEqual(result['status'], 'failed')
        self.assertEqual(result['validation_results'][0]['exit_code'], 1)

    def test_unique_isolation(self):
        root = self.root
        a = self.simulated_run(root, 'a')
        b = self.simulated_run(root, 'b')
        self.assertNotEqual(a['worktree'], b['worktree'])
        self.assertNotEqual(a['branch'], b['branch'])
        self.assertEqual(a['status'], 'awaiting_review')

    def test_decision_reject_and_retry_requires_rejected_predecessor(self):
        root = self.root
        result = self.simulated_run(root, 'retry')
        path = root / 'retry/result.json'
        with patch.object(d.sys, 'argv', ['delegate', 'decision', '--result', str(path), '--decision', 'reject']):
            self.assertEqual(d.main(), 0)
        self.assertEqual(json.loads(path.read_text())['supervisor_decision'], 'reject')
        task = dict(id='retry', description='fix', allowed_paths=['src'], acceptance_criteria=['a'], attempt=3, previous_result=str(path))
        with patch.object(d, 'executable', return_value='fake'), self.assertRaisesRegex(ValueError, 'Retry'):
            d.run_task(task, root / 'attempt3')

    def test_uuid_branches_are_concurrent_unique(self):
        root = self.root
        d.git('init', cwd=root)
        (root / 'original.txt').write_text('supervisor state')
        d.git('add', 'original.txt', cwd=root)
        d.git('-c', 'user.name=Qwen Test', '-c', 'user.email=qwen-test@example.invalid', 'commit', '-m', 'test baseline', cwd=root)
        def worker(argv, cwd, timeout, env=None):
            summary = cwd / ('.qwen-summary-' + cwd.name[5:] + '.json')
            summary.write_text(json.dumps(dict(status='completed', changes=[], tests=[], unresolved_issues=[])))
            (cwd / 'authorized.txt').write_text(cwd.name)
            return 0, '{}'
        def run(index):
            task = dict(id='task' + str(index), description='isolated', allowed_paths=['authorized.txt'], acceptance_criteria=['a'])
            return d.run_task(task, root / ('output' + str(index)))
        with patch.object(d, 'ROOT', root), patch.object(d, 'executable', return_value='fake'), patch.object(d, 'execute', side_effect=worker):
            with ThreadPoolExecutor(max_workers=2) as pool:
                results = list(pool.map(run, range(2)))
        self.assertTrue(all(r['status'] == 'awaiting_review' for r in results), results)
        self.assertNotEqual(results[0]['branch'], results[1]['branch'])
        for result in results:
            self.assertEqual((pathlib.Path(result['worktree']) / 'original.txt').read_text(), 'supervisor state')
        self.assertFalse((root / 'authorized.txt').exists())
        self.assertEqual((root / 'original.txt').read_text(), 'supervisor state')

    def test_cleanup_only_inventories(self):
        root = d.ROOT / '.claude' / 'worktrees' / ('qwen-contract-' + d.uuid.uuid4().hex[:12])
        root.mkdir(parents=True)
        (root / 'ignored-artifact').write_text('retained')
        report = root / 'result.json'
        report.write_text(json.dumps({'worktree': str(root)}))
        with patch.object(d.sys, 'argv', ['delegate', 'cleanup-inventory', '--result', str(report)]), patch('sys.stdout', new=io.StringIO()):
            self.assertEqual(d.main(), 0)
        self.assertTrue((root / 'ignored-artifact').exists())


if __name__ == '__main__':
    unittest.main()
