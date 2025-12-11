using System;
using System.Threading;
using System.Threading.Tasks;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Conductor.Test
{
	public class ConductorServiceTests
	{
		private readonly Mock<IServiceProvider> _mockServiceProvider;
		private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
		private readonly Mock<IServiceScope> _mockScope;
		private readonly Mock<IServiceProvider> _mockScopedServiceProvider;
		private readonly Mock<ILogger<ConductorService>> _mockLogger;
		private readonly Mock<IAuditLogger> _mockAuditLogger;
		private readonly ConductorService _conductorService;

		public ConductorServiceTests()
		{
			_mockServiceProvider = new Mock<IServiceProvider>();
			_mockScopeFactory = new Mock<IServiceScopeFactory>();
			_mockScope = new Mock<IServiceScope>();
			_mockScopedServiceProvider = new Mock<IServiceProvider>();
			_mockLogger = new Mock<ILogger<ConductorService>>();
			_mockAuditLogger = new Mock<IAuditLogger>();

			// Setup scope factory to return a valid scope
			_mockScopeFactory.Setup(f => f.CreateScope())
							 .Returns(_mockScope.Object);

			// Setup scope to return a valid service provider
			_mockScope.Setup(s => s.ServiceProvider)
					  .Returns(_mockScopedServiceProvider.Object);

			// Setup scoped service provider to return null for IPipelineExecutor (fallback to legacy)
			_mockScopedServiceProvider.Setup(s => s.GetService(typeof(IPipelineExecutor)))
									  .Returns((IPipelineExecutor)null);

			// Create a real instance with mocked dependencies
			_conductorService = new ConductorService(
				_mockServiceProvider.Object,
				_mockScopeFactory.Object,
				null, // CacheModule
				null, // PipelineModule
				_mockLogger.Object, // Logger
				_mockAuditLogger.Object // AuditLogger
			);
		}

		[Fact]
		public async Task Send_WithValidRequest_ShouldReturnResponse()
		{
			// Arrange
			var testRequest = new TestRequest { Id = 1, Name = "Test" };
			var testResponse = new TestResponse { Id = 1, Message = "Success" };

			// Mock the service provider to return our handler
			var mockHandler = new Mock<IRequestHandler<TestRequest, TestResponse>>();
			mockHandler.Setup(h => h.Handle(testRequest, It.IsAny<CancellationToken>()))
					   .ReturnsAsync(testResponse);
			_mockServiceProvider.Setup(s => s.GetService(typeof(IRequestHandler<TestRequest, TestResponse>)))
								.Returns(mockHandler.Object);

			// Act
			var result = await _conductorService.Send<TestResponse>(testRequest);

			// Assert
			Assert.NotNull(result);
			Assert.Equal(testResponse.Id, result.Id);
			Assert.Equal(testResponse.Message, result.Message);
		}

		[Fact]
		public async Task Send_WithNonExistentHandler_ShouldThrowInvalidOperationException()
		{
			// Arrange
			var testRequest = new TestRequest { Id = 1, Name = "Test" };

			// Mock the service provider to return null for handler
			_mockServiceProvider.Setup(s => s.GetService(typeof(IRequestHandler<TestRequest, TestResponse>)))
								.Returns((IRequestHandler<TestRequest, TestResponse>)null);

			// Act & Assert
			await Assert.ThrowsAsync<InvalidOperationException>(() => _conductorService.Send<TestResponse>(testRequest));
		}

		[Fact]
		public async Task Send_WithNullRequest_ShouldThrowArgumentNullException()
		{
			// Act & Assert
			await Assert.ThrowsAsync<ArgumentNullException>(() => _conductorService.Send<TestResponse>(null!));
		}

		[Fact]
		public async Task Send_WithValidRequest_ShouldNotThrowException()
		{
			// Arrange
			var testRequest = new TestRequest { Id = 1, Name = "Test" };
			var testResponse = new TestResponse { Id = 1, Message = "Success" };

			// Mock the service provider to return our handler
			var mockHandler = new Mock<IRequestHandler<TestRequest, TestResponse>>();
			mockHandler.Setup(h => h.Handle(testRequest, It.IsAny<CancellationToken>()))
					   .ReturnsAsync(testResponse);
			_mockServiceProvider.Setup(s => s.GetService(typeof(IRequestHandler<TestRequest, TestResponse>)))
								.Returns(mockHandler.Object);

			// Act - Should not throw
			var result = await _conductorService.Send<TestResponse>(testRequest);

			// Assert
			Assert.NotNull(result);
		}
	}

	// Test classes for unit testing
	public class TestRequest : BaseRequest
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	public class TestResponse
	{
		public int Id { get; set; }
		public string Message { get; set; } = string.Empty;
	}

	// Mock interface for testing
	public interface IRequestHandler<TRequest, TResponse>
	{
		Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
	}
}