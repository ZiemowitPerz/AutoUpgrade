using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Dzidek.Net.AutoUpgrade.Upgrader
{
    public class RetryPolicyRetriever
    {
        public static AsyncRetryPolicy GetRetryAsyncForever(ILogger logger, string message)
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryForeverAsync(
                sleepDurationProvider: GetDefaultDelay,
                onRetry: (outcome, retryAttempt, timespan) =>
                {
                    logger.LogError(
                        "Retry {RetryAttempt} after {Timespan}. {Message}\n Error: {Error}", 
                        retryAttempt, timespan, message, outcome.Message + outcome.StackTrace);
                });
        }

        public static RetryPolicy GetRetryForever(ILogger logger, string message)
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryForever( 
                sleepDurationProvider: GetDefaultDelay,
                onRetry: (outcome, retryAttempt, timespan) =>
                {
                    logger.LogError(
                        "Retry {RetryAttempt} after {Timespan}. {Message}\n Error: {Error}", 
                        retryAttempt, timespan, message, outcome.Message + outcome.StackTrace);
                });
        }
        public static RetryPolicy<List<T>> GetForeverWhenListNotEmpty<T>(ILogger logger, string message)
        {
            return Policy.
                HandleResult<List<T>>(x => x.Any())
                .WaitAndRetryForever(
                sleepDurationProvider: GetDefaultDelay,
                onRetry: (outcome, retryAttempt, timespan) =>
                {
                    logger.LogError(
                        "Retry {RetryAttempt} after {Timespan}. {Message}", retryAttempt, timespan, message);
                });
        }

        private static TimeSpan GetDefaultDelay(int retryAttempt)
        {
            return TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 30));
        }
    }
}
