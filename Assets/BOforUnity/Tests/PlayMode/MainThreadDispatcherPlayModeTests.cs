using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;
using BOforUnity.Scripts;

namespace BOforUnity.Tests.PlayMode
{
    public class MainThreadDispatcherPlayModeTests
    {
        public IEnumerator Execute_ProcessesQueuedActionsOnMainThreadInOrder()
        {
            var dispatcherGo = new GameObject("MainThreadDispatcherTest");
            var dispatcher = dispatcherGo.AddComponent<MainThreadDispatcher>();

            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int? actionThreadId = null;
            int? genericActionThreadId = null;
            string executionTrace = string.Empty;

            var worker = new Thread(() =>
            {
                MainThreadDispatcher.Execute(() =>
                {
                    actionThreadId = Thread.CurrentThread.ManagedThreadId;
                    executionTrace += "A";
                });

                MainThreadDispatcher.Execute<string>(marker =>
                {
                    genericActionThreadId = Thread.CurrentThread.ManagedThreadId;
                    executionTrace += marker;
                }, "B");
            });

            worker.Start();
            worker.Join();

            yield return null; // allow dispatcher Update to run once

            Object.DestroyImmediate(dispatcher);
            Object.DestroyImmediate(dispatcherGo);
        }
    }
}
