"""Canonical alternative-row schema, shared by validation and migration audits."""
import csv
import re
from collections import OrderedDict
from pathlib import Path

COLUMNS = ['id','line','scene','seq','speaker','trigger','channel','on','to','when','prefer','spanish','english','band','type','gender','phase','cause','job','trait','topic','core','beep','body','who','cooldown','priority','registration']
LEGACY_COLUMNS = [dict(speaker='role',type='severity',job='trait',trait='persona').get(k,k) for k in COLUMNS]
SHARED = ['trigger','channel','on','band','type','phase','cause','job','trait','topic','core','who','cooldown','priority']
DEFAULTS = dict(channel='auto',band='calm',type='chatter',gender='both',phase='any',cause='any',job='any',trait='any',topic='any',core='any')
VARIABLE = re.compile(r'\{([a-z][a-z0-9_]*)\}')
MAX_CHOICE_ALTERNATIVES = 256


def reserved_variables():
    source = (Path(__file__).resolve().parents[1] / 'src/Slots.cs').read_text()
    names = set()
    for field in ('Numbers', 'Names', 'Persons'):
        names.update(re.findall(r'"([a-z0-9_]+)"', re.search(rf'{field} = \{{(.*?)\}};', source, re.S)[1]))
    return names


def read_choices(path=None):
    path = path or Path(__file__).resolve().parents[1] / 'GameData/Lonapse/Phrases/choices.tsv'
    if not path.exists():
        return {}
    with path.open(encoding='utf-8') as stream:
        reader = csv.DictReader((line for line in stream if line.strip() and not line.lstrip().startswith('#')), delimiter='\t')
        if reader.fieldnames != ['pool', 'id', 'spanish', 'english']:
            raise ValueError(f'{path}: expected pool/id/spanish/english header')
        rows = list(reader)
    reserved = reserved_variables()
    pools, identities, texts = {}, set(), set()
    for row in rows:
        if None in row or None in row.values():
            raise ValueError(f'{path}: wrong column count')
        row = {k: v.strip() for k, v in row.items()}
        pool = row['pool']
        if not re.fullmatch('[a-z][a-z0-9_]*', pool) or pool in reserved or re.fullmatch('x_[a-z0-9_]+', pool):
            raise ValueError(f'{path}: invalid or reserved pool {pool}')
        if not re.fullmatch('[a-z0-9_]+', row['id']):
            raise ValueError(f'{path}: invalid choice identity {row["id"]}')
        slots = []
        for key in ('spanish', 'english'):
            text = row[key]
            names = set(VARIABLE.findall(text))
            if not text or '|' in text or re.search('[{}]', VARIABLE.sub('', text)) or any(
                name not in reserved and not re.fullmatch('x_[a-z0-9_]+', name) for name in names
            ):
                raise ValueError(f'{path}: {pool} needs text with only flight variables, no nested pools')
            slots.append(names)
        if slots[0] != slots[1]:
            raise ValueError(f'{path}: {pool} spoken/subtitle variables disagree')
        identity, text_key = (pool, row['id']), (pool, row['english'])
        if identity in identities or text_key in texts:
            raise ValueError(f'{path}: duplicate choice in {pool}')
        identities.add(identity)
        texts.add(text_key)
        pools.setdefault(pool, []).append((row['spanish'], row['english']))
    return pools


def expand_choices(row, pools):
    used = list(dict.fromkeys(key for key in VARIABLE.findall(row['spanish'] + row['english']) if key in pools))
    count = 1
    for key in used:
        if any('{' + key + '}' not in row[language] for language in ('spanish', 'english')):
            raise ValueError(f'{row["id"]}: phrase choices must occur in both languages')
        count *= len(pools[key])
        if count > MAX_CHOICE_ALTERNATIVES:
            raise ValueError(f'{row["id"]}: too many phrase combinations (limit {MAX_CHOICE_ALTERNATIVES})')
    alternatives = [row]
    for key in used:
        alternatives = [dict(alternative,
            spanish=alternative['spanish'].replace('{' + key + '}', es),
            english=alternative['english'].replace('{' + key + '}', en))
            for alternative in alternatives for es, en in pools[key]]
    return [dict(alternative, id=row['id'] + (f'__choice{i}' if i else '')) for i, alternative in enumerate(alternatives)]


def load(path):
    with path.open(encoding='utf-8') as stream:
        reader=csv.DictReader((line for line in stream if line.strip() and not line.startswith('#')),delimiter='\t')
        header=reader.fieldnames;rows=list(reader)
    if header != COLUMNS and header != LEGACY_COLUMNS:
        raise ValueError(f'{path}: expected the unified dialogue header')
    pools = read_choices()
    authored = rows
    rows = []
    for row in authored:
        if None in row or None in row.values():raise ValueError(f'{path}: wrong column count')
        rows.extend(expand_choices(row, pools))
    heads={};lines=OrderedDict()
    for row in rows:
        if None in row or None in row.values():raise ValueError(f'{path}: wrong column count')
        row={k:v.strip() for k,v in row.items()}
        if header == LEGACY_COLUMNS:
            old=dict(row)
            row={new:old[key] for new,key in zip(COLUMNS,LEGACY_COLUMNS)}
            row['speaker']={'crew':'kerbal_a','partner':'kerbal_b','mc':'mission_control'}.get(row['speaker'],row['speaker'])
        group=row['scene'],row['body']
        head=heads.get(group)
        if head:
            for key in SHARED:
                if not row[key]:row[key]=head[key]
        for key,value in DEFAULTS.items():row[key]=row[key] or value
        if row['phase'] in ('calm','goofy'):row['phase']='idle'
        heads.setdefault(group,dict(row))
        line=lines.setdefault(row['line'],dict(row,alternatives=[]))
        line['alternatives'].append(row)
    return header,list(lines.values())


def legacy_view(row):
    """Normalize names only at the boundary to the older scheduler/identity manifest."""
    result=dict(row)
    result.update(role={'kerbal_a':'crew','kerbal_b':'partner','mission_control':'mc'}.get(row['speaker'],row['speaker']),severity=row['type'],trait=row['job'],persona=row['trait'])
    if 'alternatives' in row:result['alternatives']=[legacy_view(alt) for alt in row['alternatives']]
    return result


def legacy_lines(path):
    """Read-only compatibility view for existing semantic audits; authored files never use parallel arrays."""
    header,lines=load(path);result=[]
    for line in lines:
        r=legacy_view(line);trigger=r['trigger'];r['id']=r['line']
        r['kind']='scene' if trigger.startswith('flight.') else trigger
        r['event']=trigger[7:] if trigger.startswith('flight.') else ''
        r['role']='crew' if r['role']=='partner' else r['role']
        r['when']=' '.join(r['when'].split());r['_when']=r['when'];r['conditions']=''
        result.append(r)
    return header,result
