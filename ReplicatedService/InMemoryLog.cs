using System;

namespace ReplicatedService
{
    //HINT: for now we are only tracking the highest replicated sequence number
    public class InMemoryLog
    {
        readonly Action<string> logger;

        public InMemoryLog(Action<string> logger)
        {
            this.logger = logger;
        }

        public long CommitLsn { get; private set; } = 1;

        public void Append(long lsn)
        {
            lock (this)
            {
                if (lsn > CommitLsn)
                {
                    CommitLsn = lsn;

                    logger($"{nameof(Append)}, [sln:{lsn}]");
                }
            }
        }
    }
}