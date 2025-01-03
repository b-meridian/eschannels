using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexLifecycleManagement;
using Elastic.CommonSchema;
using Elastic.CommonSchema.Serilog;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.CommonSchema;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Serilog;
using Serilog.Events;
using System.Threading.Channels;
using static Elastic.Channels.Diagnostics.NoopBufferedChannel;

namespace ESChannels
{
    internal class Program
    {
        /// <summary>
        /// Dummy class
        /// </summary>
        class DummyBufferedChannel : BufferedChannelBase<ChannelOptionsBase<object, object>, object, object>
        {
            public DummyBufferedChannel(ChannelOptionsBase<object, object> options) : base(options) { }

            public DummyBufferedChannel(ChannelOptionsBase<object, object> options, ICollection<IChannelCallbacks<object, object>>? callbackListeners) : base(options, callbackListeners) { }

            protected override async Task<object> ExportAsync(ArraySegment<object> buffer, CancellationToken ctx = default)
            {
                return null; // returning a new object(); here has no effect on the memory leak
            }
        }

        class DummyBufferedChannelOptions : ChannelOptionsBase<object, object> { }

        static void Main(string[] args)
        {
            // each of the following individual commented-out lines reproduces the memory leak;
            // the first line is the actual use case, followed by progressively-more-targeted
            // code zeroing in on the cause of the leak

            // ActualUseCase();

            // var sink = new ElasticsearchSink(new ElasticsearchSinkOptions());

            // var channel = new EcsDataStreamChannel<EcsDocument>(new DataStreamChannelOptions<EcsDocument>(null));

            // var noopChannel = new NoopBufferedChannel(new NoopBufferedChannel.NoopChannelOptions());

            var dummyChannelBase = new DummyBufferedChannel(new DummyBufferedChannelOptions());

            // Keep the application running in a loop to wait for the memory leak to grow:
            MockLoggingUsageLoop();
        }

        private static void ActualUseCase()
        {
            // uncomment when utilizing a self-signed cert on your cluster:
            System.Net.ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            // insert your elastic 8.x instance/cluster URL(s) in the array below;
            // elk setup in the following repo is sufficient since this is a C#-side issue:
            // https://github.com/elkninja/elastic-stack-docker-part-one
            // setup instructions here, no changes required from default repo:
            // https://www.elastic.co/blog/getting-started-with-the-elastic-stack-and-docker-compose
            var pool = new StaticNodePool(new[]
            {
                new Uri("https://elastic:changeme@localhost:9200"),
            });
            var connectionSettings = new ElasticsearchClientSettings(pool).DisableDirectStreaming();
            var elasticClient = new ElasticsearchClient(connectionSettings);

            var elasticsearchSinkOptions = new ElasticsearchSinkOptions(elasticClient.Transport)
            {
                DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName($"logstash", "logs", "results"),
                BootstrapMethod = BootstrapMethod.Failure,
                TextFormatting = new EcsTextFormatterConfiguration
                {
                    IncludeHost = false,
                    IncludeProcess = false,
                    IncludeUser = false,
                    IncludeActivityData = false,
                },
                ConfigureChannel = channelOptions =>
                {
                    channelOptions.BufferOptions = new BufferOptions
                    {
                        BoundedChannelFullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite
                    };
                    channelOptions.DisableDiagnostics = true;
                },
                MinimumLevel = LogEventLevel.Information,
            };

            var loggerConfiguration = new LoggerConfiguration();
            loggerConfiguration
                .WriteTo.Elasticsearch(elasticsearchSinkOptions);

            Serilog.Log.Logger = loggerConfiguration.CreateLogger();
        }

        /// <summary>
        /// Dummy code to keep the logging channel alive to wait for the memory leak
        /// to build up, and optionally send logs to demonstrate logging pipeline usage.
        /// </summary>
        private static void MockLoggingUsageLoop()
        {
            // a mock unique identifier to enrich messages from this process instance
            var hostIdentifier = Guid.NewGuid().ToString();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("Type a message and press enter to log, or leave blank to exit:");

                var response = Console.ReadLine();
                if (response?.Length > 0)
                {
                    Serilog.Log.ForContext("info", new Dictionary<string, object> { { "message", response } }, true)
                       .ForContext("host.hostname", hostIdentifier)
                       .Information("info");
                }
                else
                {
                    break;
                }
            }
        }
    }
}
