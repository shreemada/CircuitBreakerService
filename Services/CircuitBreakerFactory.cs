using CircuitBreakerService.Contracts;
using CircuitBreakerService.Infrastructure;
using CircuitBreakerService.Models;

namespace CircuitBreakerService.Services
{
    public class CircuitBreakerFactory : ICircuitBreakerFactory
    {
        private readonly IDistributedCircuitStateStore _stateStore;
        private readonly CircuitBreakerConfiguration _configuration;
        private readonly ILogger<DistributedCircuitBreaker> _logger;

        public CircuitBreakerFactory(
            IDistributedCircuitStateStore stateStore,
            CircuitBreakerConfiguration configuration,
            ILogger<DistributedCircuitBreaker> logger)
        {
            _stateStore = stateStore;
            _configuration = configuration;
            _logger = logger;
        }

        public DistributedCircuitBreaker CreateCircuitBreaker(string circuitName)
        {
            return new DistributedCircuitBreaker(circuitName, _stateStore, _configuration, _logger);
        }
    }
}