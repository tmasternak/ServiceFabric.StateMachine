using System;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace ReplicatedService
{
    public class CopyStateOperationStream : IOperationDataStream
    {
        InMemoryLog log;
        long startLsn;
        long endLsn;

        public CopyStateOperationStream(InMemoryLog log, long startLsn, long endLsn)
        {
            this.log = log;
            this.startLsn = startLsn;
            this.endLsn = endLsn;
        }

        public Task<OperationData> GetNextAsync(CancellationToken cancellationToken)
        {
            while (startLsn <= endLsn)
            {
                var lsnData = BitConverter.GetBytes(startLsn);
                var data =  BitConverter.GetBytes(startLsn);

                startLsn++;

                return Task.FromResult(new OperationData(new []{lsnData, data}));
            }

            return Task.FromResult((OperationData) null);
        }
    }
}