using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace ReplicatedService
{
    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            using (var runtime = FabricRuntime.Create())
            {
                runtime.RegisterStatefulServiceFactory("ReplicatedServiceType", factory: new ServiceFactory(FabricRuntime.GetNodeContext()));

                Thread.Sleep(Timeout.Infinite);
            }
        }
    }

    internal class ServiceFactory : IStatefulServiceFactory
    {
        readonly NodeContext nodeContext;

        public ServiceFactory(NodeContext nodeContext)
        {
            this.nodeContext = nodeContext;
        }

        public IStatefulServiceReplica CreateReplica(string serviceTypeName, Uri serviceName, byte[] initializationData,
            Guid partitionId, long replicaId)
        {
            ServiceEventSource.Current.Message($"Creating IStatefulServiceReplica. Type: {serviceTypeName}, uri: {serviceName}, pId: {partitionId}, rId: {replicaId}.");

            return new CustomStateReplica(nodeContext);
        }
    }

    internal class CustomStateReplica : CustomStateReplicaBase
    {
        public CustomStateReplica(NodeContext nodeContext) : base(nodeContext)
        {
            Run();
        }

        async Task Run()
        {
            while (true)
            {
                try
                {
                    if (Closed)
                    {
                        LogMessage("Replica has been closed/aborted.");
                        return;
                    }

                    if (Role == ReplicaRole.Primary)
                    {
                        var tasks = new List<Task>();

                        for (var i = 0; i < 100; i++)
                        {
                            tasks.Add(Write());
                        }

                        await Task.WhenAll(tasks);

                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception e)
                {
                    LogMessage($"Exception: {e.Message}. {e.StackTrace}.");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }
    }
}
