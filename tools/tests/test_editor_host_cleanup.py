"""Execute production teardown with isolated failures; never launch an editor."""
import ast
import copy
from pathlib import Path
import subprocess
import types
import unittest

SCRIPT=Path(__file__).resolve().parents[1]/'check-editor-game-host.py'

class CleanupTests(unittest.TestCase):
    def run_teardown(self, failure=None, forced=False):
        events=[]
        tree=ast.parse(SCRIPT.read_text())
        outer=copy.deepcopy(next(node for node in tree.body if isinstance(node,ast.Try) and any(isinstance(child,ast.Try) for child in node.body)))
        outer.body[0].body=[ast.Pass()]
        publish=copy.deepcopy(next(node for node in tree.body if isinstance(node,ast.If) and ast.unparse(node.test)=='result is not None'))
        program=ast.fix_missing_locations(ast.Module(body=[outer,publish],type_ignores=[]))
        class Process:
            pid=123
            calls=0
            def poll(self):return None
            def wait(self,timeout=None):
                events.append('process-wait');self.calls+=1
                if forced and self.calls<3:raise subprocess.TimeoutExpired('test-editor',timeout)
        class File:
            def __init__(self,label):self.label=label
            def write_bytes(self,value):
                events.append(self.label)
                if failure=='probe-write' and self.label=='probe-restored':raise OSError('probe restore failed')
            def write_text(self,value):events.append('result-written')
        def wait(*args):
            if failure=='restore-timeout':raise TimeoutError('restore timeout')
            events.append('restore-confirmed')
        def hashes():
            if failure=='hash-read':raise OSError('missing DLL')
            return ['hash']
        def call(path,method,*args):
            events.append(method)
            if method=='ExecConsoleCommand':events.append(args[0]['Command'])
        scope={'source_changed':failure=='probe-write','frontend_changed':True,'probe':File('probe-restored'),'original':b'probe',
               'frontend':File('frontend-restored'),'frontend_original':b'frontend','frontend_hashes':hashes,
               'process':Process(),'native':'native','call':call,
               'time':types.SimpleNamespace(sleep=lambda _:None),'wait':wait,'stream':types.SimpleNamespace(close=lambda:events.append('stream-closed')),
               'subprocess':subprocess,'os':types.SimpleNamespace(killpg=lambda pid,sig:events.append('signal-'+str(sig))),
               'signal':types.SimpleNamespace(SIGTERM=15,SIGKILL=9),'result':{'passed':True},'result_path':File('result'),
               'json':__import__('json'),'args':types.SimpleNamespace(game='probe'),'peak':1024,'print':lambda *a,**k:events.append('pass-printed')}
        error=None
        try:exec(compile(program,'production-teardown','exec'),scope)
        except Exception as exc:error=exc
        return events,error

    def test_restore_timeout_still_reaps_editor(self):
        events,error=self.run_teardown('restore-timeout')
        self.assertIsInstance(error,TimeoutError)
        self.assertIn('frontend-restored',events)
        self.assertIn('QUIT_EDITOR',events)
        self.assertIn('process-wait',events)
        self.assertIn('stream-closed',events)
        self.assertNotIn('result-written',events)

    def test_missing_dll_still_restores_source(self):
        events,error=self.run_teardown('hash-read')
        self.assertIsInstance(error,OSError)
        self.assertIn('frontend-restored',events)
        self.assertIn('process-wait',events)
        self.assertIn('stream-closed',events)
        self.assertNotIn('result-written',events)

    def test_first_source_failure_does_not_skip_other_cleanup(self):
        events,error=self.run_teardown('probe-write')
        self.assertIsInstance(error,OSError)
        self.assertIn('frontend-restored',events)
        self.assertIn('QUIT_EDITOR',events)
        self.assertIn('stream-closed',events)
        self.assertNotIn('result-written',events)

    def test_hung_editor_is_killed_after_restore_timeout(self):
        events,error=self.run_teardown('restore-timeout',forced=True)
        self.assertIsInstance(error,TimeoutError)
        self.assertIn('signal-15',events)
        self.assertIn('signal-9',events)
        self.assertIn('stream-closed',events)

    def test_success_is_recorded_after_cleanup(self):
        events,error=self.run_teardown()
        self.assertIsNone(error)
        self.assertLess(events.index('process-wait'),events.index('result-written'))
        self.assertLess(events.index('stream-closed'),events.index('result-written'))
        self.assertEqual(events[-1],'pass-printed')

if __name__=='__main__':unittest.main()
