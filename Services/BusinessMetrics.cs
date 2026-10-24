using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

namespace URPOnline.Services
{
    public class BusinessMetrics
    {
        private readonly IAmazonCloudWatch _cloudWatch;
        private readonly IDatabase _redis;

        public BusinessMetrics(IAmazonCloudWatch cloudWatch, IConnectionMultiplexer redis)
        {
            _cloudWatch = cloudWatch;
            _redis = redis.GetDatabase();
        }

        // 1. Métrica: CartItemsAdded
        public async Task TrackCartItemAddedAsync(int quantity = 1)
        {
            var request = new PutMetricDataRequest
            {
                Namespace = "URPOnline",
                MetricData = new List<MetricDatum>
                {
                    new MetricDatum
                    {
                        MetricName = "CartItemsAdded",
                        Value = quantity,
                        Unit = StandardUnit.Count,
                        TimestampUtc = DateTime.UtcNow
                    }
                }
            };
            await _cloudWatch.PutMetricDataAsync(request);
        }

        // 2. Métrica: ActiveCarts (Registrar actividad de la sesión)
        public async Task RegisterCartActivityAsync(string rawSessionId)
        {
            // REGLA: Nunca guardar la sesión tal cual -> aplicar Hash SHA256
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawSessionId));
            var hashedSession = Convert.ToHexString(hashBytes);

            double nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _redis.SortedSetAddAsync("active_carts", hashedSession, nowTimestamp);
        }

        // 3. Publicar ActiveCarts a CloudWatch (Ventana móvil de 5 minutos)
        public async Task PublishActiveCartsMetricAsync()
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double threshold5MinAgo = now - 300; // 300 segundos = 5 minutos

            // Eliminar registros con antigüedad mayor a 5 min
            await _redis.SortedSetRemoveRangeByScoreAsync("active_carts", 0, threshold5MinAgo);

            // Contar usuarios distintos activos
            long activeUsersCount = await _redis.SortedSetLengthAsync("active_carts");

            var request = new PutMetricDataRequest
            {
                Namespace = "URPOnline",
                MetricData = new List<MetricDatum>
                {
                    new MetricDatum
                    {
                        MetricName = "ActiveCarts",
                        Value = activeUsersCount,
                        Unit = StandardUnit.Count,
                        TimestampUtc = DateTime.UtcNow
                    }
                }
            };
            await _cloudWatch.PutMetricDataAsync(request);
        }
    }
}