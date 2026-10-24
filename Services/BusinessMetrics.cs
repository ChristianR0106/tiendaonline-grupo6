using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TiendaOnline.Services
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

        // 2. Métrica: ActiveCarts (Registrar actividad de la sesión con Hashing SHA256)
        public async Task RegisterCartActivityAsync(string rawSessionId)
        {
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

            await _redis.SortedSetRemoveRangeByScoreAsync("active_carts", 0, threshold5MinAgo);

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

        // 4. Métrica: OrdersCreated (Llamada desde CheckoutController)
        public async Task TrackOrderCreatedAsync(decimal totalAmount)
        {
            var request = new PutMetricDataRequest
            {
                Namespace = "URPOnline",
                MetricData = new List<MetricDatum>
                {
                    new MetricDatum
                    {
                        MetricName = "OrdersCreated",
                        Value = 1,
                        Unit = StandardUnit.Count,
                        TimestampUtc = DateTime.UtcNow
                    },
                    new MetricDatum
                    {
                        MetricName = "OrderValue",
                        Value = (double)totalAmount,
                        Unit = StandardUnit.None,
                        TimestampUtc = DateTime.UtcNow
                    }
                }
            };
            await _cloudWatch.PutMetricDataAsync(request);
        }
    }
}