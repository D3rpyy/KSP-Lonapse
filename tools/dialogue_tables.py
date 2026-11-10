"""Shared category-table decoding for migration audits and validation."""
import csv
import re
import json
from pathlib import Path

ALIASES = json.loads((Path(__file__).resolve().parents[1] / "tests/dialogue_id_aliases.json").read_text())

BASE_COLUMNS = ['id', 'kind', 'when', 'spanish', 'english']
META_COLUMNS = ['scene', 'seq', 'event', 'role', 'severity', 'band', 'gender', 'phase', 'cause', 'trait', 'persona', 'topic', 'core', 'beep', 'body', 'conditions']
ROUTING_COLUMNS = ['on', 'to', 'who', 'cooldown', 'priority']
SHARED = ['event', 'severity', 'band', 'phase', 'cause', 'trait', 'persona', 'topic', 'core']
DEFAULTS = dict(seq='1', event='chatter', role='crew', severity='chatter', band='calm', gender='both', phase='any', cause='any', trait='any', persona='any', topic='any', core='any', beep='', body='', conditions='')


def canonical_id(value):
    value = value[3:] if value.startswith('rt_') else value
    return ALIASES.get(value, value)


def read_rows(path):
    if 'trigger' in path.open(encoding='utf-8').readline().split('\t'):
        from unified_dialogue import legacy_lines
        return legacy_lines(path)
    with path.open(encoding='utf-8') as stream:
        reader = csv.DictReader((line for line in stream if line.strip() and not line.startswith('#')), delimiter='\t')
        headers = reader.fieldnames
        rows = list(reader)
    heads = {}
    result = []
    for row in rows:
        if None in row or None in row.values():
            raise ValueError(f'{path}: wrong column count')
        row['_when'] = row['when']
        tokens = row['when'].split()
        for field in ROUTING_COLUMNS:
            if row.get(field, '').strip():
                tokens = [t for t in tokens if not t.startswith(field+'=')]
                tokens.extend(field+'='+v for v in row[field].split())
        row['when'] = ' '.join(tokens)
        if row['kind'] not in ('scene', 'probe'):
            if row['kind'] in ('chat', 'science_talk') and not row.get('scene'):
                match = re.match(r'((?:rt_)?(?:chat|st)_[a-z0-9_]+)_([0-9]+)$', row['id'])
                if match:
                    row['scene'], row['seq'] = match.groups()
            result.append(row)
            continue
        explicit, predicates = {}, []
        conditions = [''] * len(row['english'].split('|'))
        for token in row['when'].split():
            key, sep, value = token.partition('=')
            if sep and re.fullmatch(r'v[1-9][0-9]*', key) and int(key[1:]) <= len(conditions):
                conditions[int(key[1:])-1] = value.replace(',', ' ')
            elif sep and key in META_COLUMNS:
                explicit[key] = value
            else:
                predicates.append(token)
        explicit.update({key: row[key].strip() for key in META_COLUMNS if row.get(key, '').strip()})
        values = dict(DEFAULTS, scene=row['id'])
        values['scene'] = explicit.get('scene', row['id'])
        values['body'] = explicit.get('body', '')
        group = row['kind'], values['scene'], values['body']
        if group in heads:
            values.update({key: heads[group][key] for key in SHARED})
        values.update(explicit)
        values['conditions'] = explicit.get('conditions', ' | '.join(conditions))
        if not values['conditions'].replace('|', '').strip():
            values['conditions'] = ''
        row.update(values)
        row['when'] = ' '.join(predicates)
        heads.setdefault(group, dict(values))
        result.append(row)
    return headers, result
