"""Canonical alternative-row schema, shared by validation and migration audits."""
import csv
from collections import OrderedDict

COLUMNS = ['id','line','scene','seq','speaker','trigger','channel','on','to','when','prefer','spanish','english','band','type','gender','phase','cause','job','trait','topic','core','beep','body','who','cooldown','priority','registration']
LEGACY_COLUMNS = [dict(speaker='role',type='severity',job='trait',trait='persona').get(k,k) for k in COLUMNS]
SHARED = ['trigger','channel','on','band','type','phase','cause','job','trait','topic','core','who','cooldown','priority']
DEFAULTS = dict(channel='auto',band='calm',type='chatter',gender='both',phase='any',cause='any',job='any',trait='any',topic='any',core='any')


def load(path):
    with path.open(encoding='utf-8') as stream:
        reader=csv.DictReader((line for line in stream if line.strip() and not line.startswith('#')),delimiter='\t')
        header=reader.fieldnames;rows=list(reader)
    if header != COLUMNS and header != LEGACY_COLUMNS:
        raise ValueError(f'{path}: expected the unified dialogue header')
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
