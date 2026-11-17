using System;
using System.Threading;

namespace XianAuth
{
    public sealed class SingleInstance : IDisposable
    {
        readonly Mutex _mutex;
        bool _owned;

        SingleInstance(Mutex mutex)
        {
            _mutex = mutex;
            _owned = true;
        }

        public static SingleInstance TryEnter(string name)
        {
            try
            {
                return Enter(name);
            }
            catch (UnauthorizedAccessException)
            {
                return Enter("Local\\XianRenzheng.Core");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return Enter("Local\\XianRenzheng.Core");
            }
        }

        static SingleInstance Enter(string name)
        {
            var mutex = new Mutex(false, name);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
            if (!owned)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstance(mutex);
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
                _owned = false;
            }
            _mutex.Dispose();
        }
    }
}
