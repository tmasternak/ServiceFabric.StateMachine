using System;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReplicatedService
{
    //HINT: this lacks exception handling big time
    internal abstract class CustomStateReplicaBase : IStatefulServiceReplica, IStateProvider
    {
        protected bool Closed;
        protected ReplicaRole Role;

        NodeContext nodeContext;
        StatefulServiceInitializationParameters initParameters;

        InMemoryLog log;

        FabricReplicator replicator;
        Task processingTask;
        CancellationTokenSource processingTaskCts;


        protected CustomStateReplicaBase(NodeContext nodeContext)
        {
            this.nodeContext = nodeContext;
        }

        public void Initialize(StatefulServiceInitializationParameters initParameters)
        {
            this.initParameters = initParameters;
        }

        public Task<IReplicator> OpenAsync(ReplicaOpenMode openMode, IStatefulServicePartition partition, CancellationToken cancellationToken)
        {
            LogMessage(nameof(OpenAsync));

            log = new InMemoryLog(LogMessage);

            replicator = partition.CreateReplicator(this, new ReplicatorSettings());

            return Task.FromResult((IReplicator)replicator);
        }

        public async Task<string> ChangeRoleAsync(ReplicaRole newRole, CancellationToken cancellationToken)
        {
            Role = newRole;

            LogMessage($"{nameof(ChangeRoleAsync)}. Role ${newRole}");

            if (Role == ReplicaRole.Primary || Role == ReplicaRole.None)
            {
                await StopProcessing();
            }

            if (Role == ReplicaRole.IdleSecondary)
            {
                StartProcessingStateCopyFromPrimary();
            }

            if (Role == ReplicaRole.ActiveSecondary)
            {
                StartProcessingReplicationFromPrimary();
            }

            //HINT: This is what get's registered in the NamingService for this replica instance
            return "I-dont-care-for-now";
        }

        Task StopProcessing()
        {
            if (processingTask == null)
            {
                return Task.FromResult(true);
            }

            processingTaskCts.Cancel();

            return processingTask;
        }

        void StartProcessingReplicationFromPrimary()
        {
            LogMessage(nameof(StartProcessingReplicationFromPrimary));

            processingTaskCts = new CancellationTokenSource();

            processingTask = Task.Run(async () =>
            {
                IOperationStream replicationStream = null;

                var isSlow = !this.nodeContext.NodeName.EndsWith("4");

                while (!processingTaskCts.IsCancellationRequested)
                {
                    try
                    {
                        replicationStream = replicationStream ?? replicator.StateReplicator.GetReplicationStream();

                        var operation = await replicationStream.GetOperationAsync(processingTaskCts.Token);

                        if (isSlow)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30));
                        }

                        var sln = operation.SequenceNumber;
                        var value = operation.Data.First().Array[0];

                        log.Append(sln);

                        operation.Acknowledge();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error procesing replication stream ${ex.Message}. ${ex.StackTrace}.");

                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
            });
        }

        void StartProcessingStateCopyFromPrimary()
        {
            LogMessage(nameof(StartProcessingStateCopyFromPrimary));

            processingTaskCts = new CancellationTokenSource();

            processingTask = Task.Run(async () =>
            {
                var catchupStream = replicator.StateReplicator.GetCopyStream();

                while (!processingTaskCts.Token.IsCancellationRequested)
                {
                    var operation = await catchupStream.GetOperationAsync(processingTaskCts.Token);

                    if (operation == null)
                    {
                        break;
                    }

                    var sln = BitConverter.ToInt32(operation.Data[0].Array, 0);
                    var value = operation.Data[1].Array[0];

                    log.Append(sln);

                    operation.Acknowledge();
                }
            });
        }

        //HINT: close/end the replication tasks
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            Closed = true;

            LogMessage(nameof(CloseAsync));

            return Task.FromResult(0);
        }

        public void Abort()
        {
            Closed = true;

            LogMessage(nameof(Abort));
        }

        public long GetLastCommittedSequenceNumber()
        {
            return log.CommitLsn;
        }

        public Task UpdateEpochAsync(Epoch epoch, long previousEpochLastSequenceNumber, CancellationToken cancellationToken)
        {
            LogMessage($"{nameof(UpdateEpochAsync)}. Epoch: {epoch}. Esn: {previousEpochLastSequenceNumber}");

            return Task.FromResult(0);
        }

        public Task<bool> OnDataLossAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public IOperationDataStream GetCopyContext()
        {
            LogMessage(nameof(GetCopyContext));

            return new CopyContextOperationStream(log.CommitLsn + 1);
        }

        public IOperationDataStream GetCopyState(long upToSequenceNumber, IOperationDataStream copyContext)
        {
            LogMessage(nameof(GetCopyState));

            var startData = copyContext.GetNextAsync(CancellationToken.None).GetAwaiter().GetResult();

            var startLsn = BitConverter.ToInt64(startData[0].Array, 0);

            return new CopyStateOperationStream(log, startLsn, upToSequenceNumber);
        }

        //TODO: this has not concurrency control what-so-ever
        //TODO: is ordering of replication requests important
        protected async Task Write()
        {
            long lsn;

            await replicator.StateReplicator.ReplicateAsync(new OperationData(new[] {(byte)13}), CancellationToken.None, out lsn);

            log.Append(lsn);
        }

        protected Task<long> Read()
        {
            return Task.FromResult(log.CommitLsn);
        }

        protected void LogMessage(string message)
        {
            var context = new StatefulServiceContext(nodeContext, initParameters.CodePackageActivationContext, initParameters.ServiceTypeName, initParameters.ServiceName, initParameters.InitializationData, initParameters.PartitionId, initParameters.ReplicaId);

            var logMessage = $"[n:{nodeContext.NodeName}] $[r:{Role}] $[rid:{context.ReplicaId}], ${message}";

            ServiceEventSource.Current.ServiceMessage(context, logMessage);
        }
    }
}