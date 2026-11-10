#!/usr/bin/env python3
"""Validate category tables and the original identity contract; no speech renderer required."""
import json
import pathlib
import re
import sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from dialogue_tables import BASE_COLUMNS, META_COLUMNS, ROUTING_COLUMNS, ALIASES, canonical_id, read_rows

ROOT = pathlib.Path(__file__).resolve().parents[1]
BASE = ROOT / 'GameData/Lonapse/Phrases'
CONTRACT_FIELDS = ['id', 'scene', 'seq', 'event', 'role', 'core', 'beep', 'phase', 'cause', 'trait', 'persona', 'topic', 'body']


def validate():
    from unified_dialogue import load, legacy_view, COLUMNS
    errors, entries, categories, all_ids, line_ids, references = [], {}, 0, set(), set(), []
    paths = []
    for folder in ('Crew', 'Probe'):
        files = sorted((BASE / folder).glob('*.tsv'))
        if not files: errors.append(f'{folder}: no category tables')
        paths.extend(files)
    paths.extend(sorted((ROOT / 'GameData/Lonapse/Compat').glob('*/*.tsv')))
    alternatives = 0
    for path in paths:
        categories += 1
        try: headers, lines = load(path)
        except (ValueError, KeyError) as exc:
            errors.append(str(exc)); continue
        positions=set()
        for canonical_line in lines:
            line=legacy_view(canonical_line)
            key=line['line'];trigger=line['trigger']
            if key in line_ids:errors.append(f'duplicate logical line {key}')
            line_ids.add(key)
            if key!=canonical_id(key):errors.append(f'{key}: noncanonical line ID')
            position=(trigger,line['scene'],line['seq'],line['body'])
            if position in positions:errors.append(f'{key}: conflicting scene position')
            positions.add(position)
            shared=['scene','seq','role','trigger','channel','on','to','band','severity','gender','phase','cause','trait','persona','topic','core','beep','body','who','cooldown','priority','registration']
            for alt in line['alternatives']:
                alternatives+=1
                if alt['id'] in all_ids:errors.append(f"duplicate alternative {alt['id']}")
                all_ids.add(alt['id'])
                if any(alt[k]!=line[k] for k in shared):errors.append(f'{key}: alternatives disagree on line metadata')
                if any('|' in alt[k] for k in ('spanish','english','when')):errors.append(f'{key}: pipe-delimited alternatives are retired')
                if not alt['prefer'].lstrip('-').isdigit():errors.append(f'{key}: invalid preference')
                if trigger not in ('meta','experiment','device','contract') and (not alt['english'] or alt['role']!='probe' and not alt['spanish']):errors.append(f'{key}: missing paired dialogue')
                if re.search(r'\{(?:el |un |del |al )|\{[^}]+:\d+\}',alt['spanish']):errors.append(f'{key}: unresolved vocabulary pool')
            if trigger.startswith('flight.') or trigger=='probe':
                value=dict(line,id=key,event=trigger[7:] if trigger.startswith('flight.') else 'probe_chat')
                value['role']='crew' if value['role']=='partner' else value['role'];entries[key]=value
            for target in line['to'].split():references.append((key,target))
    for key,target in references:
        canonical=canonical_id(target)
        if target!=canonical:errors.append(f'{key}: legacy reply target {target}')
        matches=any(value.startswith(canonical[:-1]) for value in line_ids) if canonical.endswith('*') else canonical in line_ids
        if not matches:errors.append(f'{key}: unknown reply target {target}')
    manifest = json.loads((ROOT / 'tests/runtime_dialogue_contract.json').read_text())
    for original in manifest:
        key = canonical_id(original[0])
        current = entries.get(key)
        if current is None:
            errors.append(f'missing migrated line {original[0]} (now {key})')
            continue
        for field, expected in zip(CONTRACT_FIELDS[1:], original[1:]):
            if field == 'scene':
                expected = canonical_id(expected)
            if field == 'phase' and expected in ('calm','goofy'):
                expected = 'idle'
            if field == 'body' and original[0].removeprefix('rt_') in ALIASES:
                expected = ''  # the authored {body} template now works on every body
            if current.get(field, '') != expected:
                errors.append(f'{key}: changed {field}: {expected!r} -> {current.get(field)!r}')
    runtime_aliases = dict(re.findall(r'\{ "([^"\n]+)", "([^"\n]+)" \}', (ROOT / 'src/DialogueIds.cs').read_text()))
    if runtime_aliases != ALIASES:
        errors.append('runtime ID alias map and migration contract disagree')
    for old, target in ALIASES.items():
        if target not in entries:
            errors.append(f'alias {old} has no replacement {target}')
    for retired in ('phrases.tsv', 'pools.tsv', 'Runtime', 'Runtime_Probe'):
        if (BASE / retired).exists():
            errors.append(f'retired dialogue source still ships: {retired}')
    for retired in ('crew', 'control'):
        if (ROOT / 'GameData/Lonapse/Sounds' / retired).exists():
            errors.append(f'recorded speech still ships: Sounds/{retired}')
    for error in errors:
        print(f'ERROR: {error}', file=sys.stderr)
    print(f'Dialogue: {categories} unified tables, {len(line_ids)} lines / {alternatives} alternatives, {len(manifest)} original identities checked, {len(errors)} errors')
    return bool(errors)


if __name__ == '__main__':
    sys.exit(validate())
