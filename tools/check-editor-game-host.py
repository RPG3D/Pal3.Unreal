"""Exercise ordinary UnrealSharp startup, game selection and upstream hot reload."""
import argparse,hashlib,json,os,signal,socket,subprocess,time,urllib.request
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--editor',type=Path,required=True)
parser.add_argument('--game',choices=['pal3','pal3a'],default='pal3')
parser.add_argument('--data-path',type=Path)
parser.add_argument('--output',type=Path)
parser.add_argument('--check-hot-reload',action='store_true')
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
out=(args.output or root/'Saved/Review'/('OrdinaryEditor-'+args.game+'-'+time.strftime('%Y%m%d-%H%M%S'))).resolve();out.mkdir(parents=True,exist_ok=True)
log=out/'Editor.log';log.unlink(missing_ok=True)
result_path=out/'Result.json';result_path.unlink(missing_ok=True)
with socket.socket() as listener:listener.bind(('127.0.0.1',0));port=listener.getsockname()[1]
env=os.environ.copy()
if args.data_path:env[args.game.upper()+'_DATA_PATH']=str(args.data_path.resolve())
command_line=[str(args.editor.resolve()),str(root/'SharpDemo.uproject'),'-UserDir='+str(out/'User'),'-unattended','-nosplash','-nosound','-ini:RemoteControl:[/Script/RemoteControlCommon.RemoteControlSettings]:RemoteControlHttpServerPort='+str(port),'-ExecCmds=WebControl.StartServer','-abslog='+str(log)]
probe=root/'Script/PluginRegression/ReloadProbe.cs';original=probe.read_bytes();source_changed=False
frontend=root/'Script/Pal3.Frontend/APal3Driver.cs';frontend_original=frontend.read_bytes();frontend_changed=False
frontend_dlls=[root/'Binaries/Managed/net10.0'/name for name in ['Pal3.Frontend.dll','Pal3A.Frontend.dll']]
def frontend_hashes():return [hashlib.sha256(path.read_bytes()).hexdigest() for path in frontend_dlls]

stream=(out/'stdout.log').open('w')
try:process=subprocess.Popen(command_line,cwd=root,env=env,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
except BaseException:stream.close();raise
peak=0;host=None;native='/Script/SharpDemo.Default__Pal3DebugOverlay'

def text():return log.read_text(errors='replace') if log.exists() else ''
def call(path,function,parameters=None):
    request=urllib.request.Request(f'http://127.0.0.1:{port}/remote/object/call',data=json.dumps({'objectPath':path,'functionName':function,'parameters':parameters or {}}).encode(),headers={'Content-Type':'application/json'},method='PUT')
    with urllib.request.urlopen(request,timeout=20) as response:return json.load(response)
def sample():
    global peak
    data=subprocess.run(['ps','-p',str(process.pid),'-o','rss=,%cpu='],capture_output=True,text=True).stdout.strip().split()
    if data:
        peak=max(peak,int(data[0]))
        with (out/'memory.jsonl').open('a') as f:f.write(json.dumps({'time':time.time(),'rssKiB':int(data[0]),'cpu':float(data[1])})+'\n')
        if peak>12*1024*1024:raise RuntimeError('Editor exceeded 12 GiB RSS guard')
def wait(condition,label,seconds=180):
    end=time.monotonic()+seconds;last=''
    while time.monotonic()<end:
        sample()
        if process.poll() is not None:raise RuntimeError('Editor exited during '+label)
        if 'Fatal error:' in text():raise RuntimeError('Fatal during '+label)
        try:
            value=condition()
            if value:return value
        except (OSError,ValueError,KeyError) as error:last=str(error)
        time.sleep(1)
    raise TimeoutError(label+': '+last)
def command(name,argument=''):
    value=json.loads(call(host,'ExecuteGameCommand',{'command':name,'argument':argument})['ReturnValue'])
    if name!='host-status' and not value.get('ok'):raise RuntimeError(str(value))
    return value

def probe_revisions():
    return [call('/Script/UnrealSharp.Default__'+name+'_C','GetRevision')['ReturnValue'] for name in ['ReloadProbe','SecondReloadProbe']]
result=None
try:
    try:
        print('START',args.game,process.pid,flush=True)
        wait(lambda:'C# Hot Reload is ready' in text(),'workspace',240)
        saved=wait(lambda:call('/Script/SharpDemo.Default__ProjectFileLibrary','GetSavedDirectory')['ReturnValue'],'HTTP and native API',90)
        assert Path(saved).resolve().is_relative_to((out/'User').resolve()),saved
        if args.check_hot_reload:
            assert wait(probe_revisions,'ordinary reflected probes')==['revision-1','revision-1']
            completed=text().count('C# Hot Reload completed')
            probe.write_bytes(original.replace(b'revision-1',b'revision-2'));source_changed=True
            wait(lambda:text().count('C# Hot Reload completed')>completed,'source-save hot reload')
            wait(lambda:probe_revisions()==['revision-2','revision-2'],'both projects rebound')
            completed=text().count('C# Hot Reload completed')
            probe.write_bytes(original);source_changed=False
            wait(lambda:text().count('C# Hot Reload completed')>completed,'restore hot reload')
            wait(lambda:probe_revisions()==['revision-1','revision-1'],'restored functions')
            print('PASS ordinary hot reload in both projects',flush=True)
            hashes=frontend_hashes()
            frontend_completed=text().count('C# Hot Reload completed')
            marker=b'BeginPlay - starting managed runtime smoke probe'
            assert marker in frontend_original
            frontend.write_bytes(frontend_original.replace(marker,marker+b' [ordinary-hot-reload]'));frontend_changed=True
            wait(lambda:all(a!=b for a,b in zip(frontend_hashes(),hashes)),'both game Frontend DLLs recompiled')
            wait(lambda:text().count('C# Hot Reload completed')>=frontend_completed+2,'both frontend reloads completed')
            print('PASS actual PAL3/PAL3A frontend recompilation',flush=True)

        assert 'An explicit assembly set is active' not in text()
        assert 'Full boot starting' not in text(),'Loading code unexpectedly started a game'
        call(native,'StartPieSession')
        def find_host():
            paths=call(native,'FindActorPaths',{'NameContains':'GameHostDriver','MaxCount':2})['ReturnValue']
            return next((p.split(' |')[0] for p in paths.splitlines() if 'PersistentLevel' in p),None)
        host=wait(find_host,'game host')
        assert not command('host-status')['running']
        menu=wait(lambda:command('host-ui')['result'] if command('host-ui')['result']['visible'] else None,'menu')
        assert {x['id'] for x in menu['games']}=={'pal3','pal3a'},menu
        for game in ['pal3','pal3a']:
            command('host-click','host_game:'+game);assert not command('host-status')['running']
        command('host-click','host_game:'+args.game);command('host-click','host_start')
        def title():
            state=command('status')['result']
            if state.get('bootError') or state.get('sceneLoadError'):raise RuntimeError(str(state))
            return state if state.get('state')=='Title' and not state.get('loadingScene') else None
        state=wait(title,'game title',240)
        expected=('q01','yn09a') if args.game=='pal3' else ('q02','qn08y')
        assert (state['city'].lower(),state['scene'].lower())==expected,state
        if args.check_hot_reload:assert '[ordinary-hot-reload]' in text(),'Game actor did not execute the new frontend body'
        (out/'title.json').write_text(json.dumps(state,ensure_ascii=False,indent=2))
        for i in range(3):command('gc');time.sleep(1);sample();assert command('status')['result']['state']=='Title'
        command('scene','1')
        def gameplay():
            state=command('status')['result']
            if state.get('sceneLoadError'):raise RuntimeError(str(state))
            return state if state.get('state')=='Gameplay' and not state.get('loadingScene') and state.get('playerHasControl') else None
        scene=wait(gameplay,'first debug scene')
        command('save','99');command('load','99');loaded=wait(gameplay,'save reload')
        for i in range(3):command('gc');time.sleep(1);sample()
        result={'game':args.game,'passed':True,'hotReload':args.check_hot_reload,'peakRssKiB':peak,'title':expected,'scene':scene,'loaded':loaded,'log':str(log)}

    finally:
        try:
            if source_changed:probe.write_bytes(original)
        finally:
            if frontend_changed:
                if process.poll() is None:
                    try:call(native,'StopPieSession');time.sleep(2)
                    except Exception:pass
                try:hashes=frontend_hashes()
                finally:frontend.write_bytes(frontend_original)
                if process.poll() is None:
                    wait(lambda:all(a!=b for a,b in zip(frontend_hashes(),hashes)),'restore both frontend bodies')

finally:
    try:
        if process.poll() is None:
            try:call(native,'StopPieSession');call(native,'ExecConsoleCommand',{'Command':'QUIT_EDITOR'})
            except Exception:pass
            try:process.wait(timeout=25)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid,signal.SIGTERM)
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait()

    finally:
        stream.close()

if result is not None:
    result_path.write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print('PASS',args.game,'peak RSS',round(peak/1024),'MiB',flush=True)
