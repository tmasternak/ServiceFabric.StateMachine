using System;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace ReplicatedService
{
    public class CopyContextOperationStream : IOperationDataStream
    {
        OperationData operationData;
        bool stop;

        public CopyContextOperationStream(long lastLsn)
        {
            operationData = new OperationData(BitConverter.GetBytes(lastLsn));
        }

        public Task<OperationData> GetNextAsync(CancellationToken cancellationToken)
        {
            if (!stop)
            {
                stop = true;

                return Task.FromResult(operationData);
            }

            return Task.FromResult((OperationData)null);
        }
    }
}