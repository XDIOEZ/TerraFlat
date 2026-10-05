"""python .agents/skills/game-developer-loop/scripts/collect_drop.py Ore_Iron"""
import sys,json
from gameplay_client import call,go

def quantity(observation,item):
    return sum(v['n'] for v in observation['player'].get('inventory',[]) if v['id']==item)

def collect(item):
    before=call('gameplay_observe',profile='full',includeInventory=True)
    matches=call('gameplay_query',source='drops',itemId=item,radius=64,limit=1).get('matches',[])
    if not matches:return {'picked':False,'reason':'没有已加载掉落物'}
    drop=matches[0]
    go(drop['position']['x'],drop['position']['y'])
    call('gameplay_act',action='wait',seconds=.5)
    after=call('gameplay_observe',profile='full',includeInventory=True)
    delta=quantity(after,item)-quantity(before,item)
    return {'picked':delta>0,'added':delta,'carryCapacity':after['player'].get('carryCapacity'),
            'reason':'库存增加' if delta>0 else '已到达但未拾取，检查容量与掉落状态'}

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    result=collect(sys.argv[1]);print(json.dumps(result,ensure_ascii=False))
    sys.exit(0 if result['picked'] else 2)
