using MasterRedisAPI.Helper;
using MasterRedisAPI.Models;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#region Services & Configuration

// Add OpenAPI/Swagger services
builder.Services.AddOpenApi();

// Add MVC controllers
builder.Services.AddControllers();

// Support command-line configuration
builder.Configuration.AddCommandLine(args);

#endregion

#region Redis Node Options Binding

// Bind RedisNodeOptions from command-line arguments
builder.Services.Configure<RedisNodeOptions>(options =>
{
    string? role = builder.Configuration["role"];
    string? consumer = builder.Configuration["consumer"];

    // Validate role argument (master/child)
    if (!Enum.TryParse(role, true, out RedisEnumRole parsedRole))
        throw new Exception("Invalid --role argument. Use 'master' or 'child'.");

    options.Role = parsedRole;

    // If consumer name is provided, override default
    options.ConsumerName = consumer ?? options.ConsumerName;
});

#endregion

#region Master API Options

// Configure master API options for child scheduler
var masterApiOptions = new MasterApiOptions
{
    BaseUrl = "http://localhost:5000/",
    ConsumerGroup = "child-group",
    ConsumerName = "child-1",
    BatchSize = 50,
    PollIntervalSeconds = 2,
};

// Register as singleton for DI
builder.Services.AddSingleton(masterApiOptions);

#endregion

#region HTTP Client for Child Scheduler

// Add typed HttpClient for ChildStreamScheduler
builder.Services.AddHttpClient<ChildStreamScheduler>(client =>
{
    Console.WriteLine(masterApiOptions.BaseUrl);
    client.BaseAddress = new Uri(masterApiOptions.BaseUrl);
});

// Register the background hosted service
builder.Services.AddHostedService<ChildStreamScheduler>();

#endregion

#region Build Web Application

WebApplication app = builder.Build();

#endregion

#region Middleware & Pipeline

// Swagger/OpenAPI setup for development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Master API";
    });

    app.UseDeveloperExceptionPage();
}

// Display Redis password from configuration (debug only)
Console.WriteLine(builder.Configuration["redisPassword"]);

#endregion

#region Redis Configuration

// Bind Redis connection options from configuration
RedisConnectionOptions redisOptions = new()
{
    Host = builder.Configuration["redisHost"] ?? "localhost",
    Port = int.Parse(builder.Configuration["redisPort"] ?? "6379"),
    User = builder.Configuration["redisUser"],
    Password = builder.Configuration["redisPassword"],
};

// Initialize singleton CacheManager with Redis config
CacheManager.Configure(redisOptions);

#endregion

#region HTTP Pipeline

app.UseHttpsRedirection();
app.UseRouting();
app.MapControllers();

#endregion

// Run the application
app.Run();
