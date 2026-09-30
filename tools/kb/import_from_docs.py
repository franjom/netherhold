#!/usr/bin/env python3
"""Create a draft record for every FUN_ cited in docs/research that has none.

    python tools/kb/import_from_docs.py [--dry-run]

Each draft is GUESS/READ: its role is the sentence of the first citation, in
the doc's own words, and its evidence is that doc's nearest heading above the
citation. Existing records are never touched. Run `dk2tools kb check --all`
and `dk2tools kb index` afterwards.
"""
import datetime
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
RESEARCH = os.path.join(REPO, 'docs', 'research')
FUNCTIONS = os.path.join(RESEARCH, 'functions')
MENTION = re.compile(r'\bFUN_(00[4-6][0-9A-Fa-f]{5})\b')
HEADING = re.compile(r'^#{1,6}\s')
FENCE = re.compile(r'^\s*(```|~~~)')


def slug(heading):
    t = heading.lstrip('#').strip().lower()
    t = re.sub(r'[^a-z0-9_ -]', '', t)      # same rule as FunctionKb.Slug: underscores survive
    return t.replace(' ', '-')


def sentence_around(line, address):
    """The sentence of the line that holds the mention, backticks and pipes stripped."""
    text = line.replace('`', '').replace('|', ' ').strip()
    for part in re.split(r'(?<=[.;])\s+', text):
        if address.lower() in part.lower():
            return re.sub(r'\s+', ' ', part).strip(' -*')[:200]
    return re.sub(r'\s+', ' ', text)[:200]


def first_citations():
    """address -> (evidence link, sentence, doc name), from the first doc that cites it."""
    found = {}
    for name in sorted(os.listdir(RESEARCH)):
        if not name.endswith('.md'):
            continue
        heading = None
        in_fence = False
        with open(os.path.join(RESEARCH, name), encoding='utf-8-sig') as stream:
            for line in stream:
                if FENCE.match(line):
                    in_fence = not in_fence
                # A '# comment' inside a fence is code, not a heading; a mention there is still a citation.
                if not in_fence and HEADING.match(line):
                    heading = slug(line)
                for m in MENTION.finditer(line):
                    address = m.group(1).upper()
                    if address not in found:
                        anchor = '#' + heading if heading else ''
                        found[address] = ('docs/research/' + name + anchor,
                                          sentence_around(line, 'FUN_' + address), name)
    return found


def main():
    dry = '--dry-run' in sys.argv
    today = datetime.date.today().isoformat()
    os.makedirs(FUNCTIONS, exist_ok=True)
    created = 0
    for address, (evidence, sentence, doc) in sorted(first_citations().items()):
        path = os.path.join(FUNCTIONS, address + '.md')
        if os.path.exists(path):
            continue
        sentence = sentence.replace('\n', ' ').replace('\r', ' ').strip()
        if not sentence:
            sentence = 'cited in ' + doc
        record = (
            f'address: 0x{address}\n'
            f'name: FUN_{address}\n'
            f'role: {sentence}\n'
            f'confidence: GUESS\n'
            f'status: READ\n'
            f'evidence: {evidence}\n'
            f'\n'
            f'Imported from the first citation in docs/research; the role is that sentence, not a verdict. '
            f'Raise the confidence only after reading the evidence section.\n'
            f'\n'
            f'## History\n'
            f'- {today} import: drafted by tools/kb/import_from_docs.py from {evidence.split("#")[0]}\n'
        )
        created += 1
        if dry:
            print(address, evidence)
        else:
            with open(path, 'w', encoding='utf-8', newline='\n') as stream:
                stream.write(record)
    print('%s %d records' % ('would create' if dry else 'created', created))


if __name__ == '__main__':
    main()
