using System.Net;
using System.Text;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeConversionJobServiceTests
{
    [Fact]
    public async Task Job_service_proxies_real_progress_and_completed_markdown()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """
            {"job_id":"all2md-1","status":"queued","progress":5,"message":"Queued conversion job"}
            """),
            Json(HttpStatusCode.OK, """
            {"job_id":"all2md-1","status":"running","progress":67,"message":"Converting document to Markdown (220s elapsed)"}
            """),
            Json(HttpStatusCode.OK, """
            {"job_id":"all2md-1","status":"completed","progress":100,"message":"Conversion completed","result":{"filename":"resume.pdf","content":"# Resume","content_type":"application/pdf"}}
            """));
        var service = CreateService(handler, new ManualTimeProvider(DateTimeOffset.Parse("2026-07-09T00:00:00Z")));

        var started = await service.StartAsync("huangd", CreateFile(), CancellationToken.None);
        var running = await service.GetAsync("huangd", started.JobId, CancellationToken.None);
        var completed = await service.GetAsync("huangd", started.JobId, CancellationToken.None);

        Assert.Equal(ResumeConversionJobStatuses.Queued, started.Status);
        Assert.Equal(5, started.Progress);
        Assert.Equal(67, running?.Progress);
        Assert.Equal("Converting document to Markdown (220s elapsed)", running?.Message);
        Assert.Equal(ResumeConversionJobStatuses.Completed, completed?.Status);
        Assert.Equal("# Resume", completed?.Result?.Markdown);
        Assert.Equal(ResumeLanguages.English, completed?.Result?.DetectedLanguage);
    }

    [Fact]
    public async Task Job_service_hides_jobs_from_other_tenants()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """
        {"job_id":"all2md-2","status":"queued","progress":5,"message":"Queued conversion job"}
        """));
        var service = CreateService(handler, new ManualTimeProvider(DateTimeOffset.UtcNow));
        var started = await service.StartAsync("huangd", CreateFile(), CancellationToken.None);

        var result = await service.GetAsync("another-user", started.JobId, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Job_service_uses_configured_timeout_as_job_lifetime()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """
        {"job_id":"all2md-3","status":"queued","progress":5,"message":"Queued conversion job"}
        """));
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-09T00:00:00Z"));
        var service = CreateService(handler, timeProvider);
        var started = await service.StartAsync("huangd", CreateFile(), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(601));

        var result = await service.GetAsync("huangd", started.JobId, CancellationToken.None);

        Assert.Equal(ResumeConversionJobStatuses.Failed, result?.Status);
        Assert.Equal(100, result?.Progress);
        Assert.Contains("600 seconds", result?.Error, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    private static ResumeConversionJobService CreateService(HttpMessageHandler handler, TimeProvider timeProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["All2MD:ApiUrl"] = "http://all2md.test",
                ["All2MD:TimeoutSeconds"] = "600"
            })
            .Build();
        return new ResumeConversionJobService(
            new FakeHttpClientFactory(handler),
            configuration,
            timeProvider,
            NullLogger<ResumeConversionJobService>.Instance);
    }

    private static IFormFile CreateFile()
    {
        var bytes = Encoding.UTF8.GetBytes("fake pdf");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "resume.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
