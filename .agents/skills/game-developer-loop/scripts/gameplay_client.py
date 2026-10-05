"""从项目根目录运行；只调用正式 GamePlayMCP，失败不重放写操作。"""
import json,socket,struct

def receive(sock,n):
    data=b''
    while len(data)<n:
        part=sock.recv(n-len(data))
        if not part:raise RuntimeError('bridge closed; 查询现场后再决定下一步，禁止盲目重放动作')
        data+=part
    return data

def call(tool,**params):
    with socket.create_connection(('127.0.0.1',6400),timeout=5) as sock:
        sock.settimeout(100)
        while receive(sock,1)!=b'\n':pass
        packet=json.dumps({'type':tool,'params':params},ensure_ascii=False).encode()
        sock.sendall(struct.pack('>Q',len(packet))+packet)
        envelope=json.loads(receive(sock,struct.unpack('>Q',receive(sock,8))[0]))
    result=envelope.get('result',{})
    value=result.get('data',envelope)
    if envelope.get('status')=='error' or result.get('success') is False or isinstance(value,dict) and value.get('ok') is False:
        raise RuntimeError(json.dumps(envelope,ensure_ascii=False))
    return value

def go(x,y):
    for _ in range(32):
        action=call('gameplay_act',action='move_to',x=x,y=y,seconds=20)
        state=action.get('data',{})
        if state.get('reached'):return
        if not state.get('timedOut') or state.get('progressDistance',0)<.1:
            raise RuntimeError('导航未到达: '+json.dumps(state,ensure_ascii=False))
    raise RuntimeError('移动超过分段上限')
