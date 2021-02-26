using NBomber;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Plugins.Http.CSharp;
using NBomber.Plugins.Network.Ping;
using Newtonsoft.Json;
using System;
using System.Linq;
using Xunit;

namespace Pacco.Services.Availability.Tests.Performance
{
    public class PerformanceTests
    {
        public class UserResponse
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
        }

        public class PostResponse
        {
            public string Id { get; set; }
            public string UserId { get; set; }
            public string Title { get; set; }
            public string Body { get; set; }
        }

        [Fact]
        public void UsersLoadTest()
        {
            const int rate = 100;
            const string baseUrl = "https://jsonplaceholder.typicode.com";
            const int duration = 5;
            const int expectedRps = 80;
            const int expectedLatency = 500;

            var userFeed = Feed.CreateRandom(
                 name: "userFeed",
                 provider: FeedData.FromSeq(new[] { "1", "2", "3", "4", "5" })
             );

            IStep getUser = CreateGetUserStep(baseUrl, userFeed);
            IStep getPosts = CreateGetPostsStep(baseUrl);

            var scenario = ScenarioBuilder
                .CreateScenario("scenario", getUser, getPosts)
                .WithWarmUpDuration(TimeSpan.FromSeconds(5))
                .WithLoadSimulations(new[]
                {
                    Simulation.InjectPerSec(rate: rate, during: TimeSpan.FromSeconds(duration))
                });

            var pingPluginConfig = PingPluginConfig.CreateDefault(new[] { baseUrl });
            var pingPlugin = new PingPlugin(pingPluginConfig);

            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithWorkerPlugins(pingPlugin)
                .WithTestSuite("http")
                .WithTestName("advanced_test")
                .Run();

            //Check if 95% of requests had latency below
            Assert.True(stats.ScenarioStats.All(scenarioStats => scenarioStats.StepStats.All(stepStats => stepStats.Percent95 <= expectedLatency)));

            //Check if requests per seconds are over expected value
            Assert.True(stats.ScenarioStats.All(scenarioStats => scenarioStats.StepStats.All(stepStats => stepStats.RPS >= expectedRps)));
        }

        private static IStep CreateGetPostsStep(string baseUrl)
        {
            var getPosts = HttpStep.Create("get_posts", context =>
            {
                var user = context.GetPreviousStepResponse<UserResponse>();
                var url = $"{baseUrl}/posts?userId={user.Id}";

                return Http.CreateRequest("GET", url)
                    .WithCheck(async response =>
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        // parse JSON
                        var posts = JsonConvert.DeserializeObject<PostResponse[]>(json);

                        return posts?.Length > 0
                            ? Response.Ok()
                            : Response.Fail($"not found posts for user: {user.Id}");
                    });
            });
            return getPosts;
        }

        private static IStep CreateGetUserStep(string baseUrl, IFeed<string> userFeed)
        {
            var getUser = HttpStep.Create("get_user", userFeed, context =>
            {
                var userId = context.FeedItem;
                var url = $"{baseUrl}/users?id={userId}";

                return Http.CreateRequest("GET", url)
                    .WithCheck(async response =>
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        var users = JsonConvert.DeserializeObject<UserResponse[]>(json);

                        return users?.Length == 1
                            ? Response.Ok(users.First()) // we pass user object response to the next step
                            : Response.Fail($"not found user: {userId}");
                    });
            });
            return getUser;
        }
    }
}