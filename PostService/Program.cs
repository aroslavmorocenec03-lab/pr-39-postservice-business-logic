using System.Text.Json.Serialization;
using PostService.Dtos;
using PostService.Infrastructure;
using PostService.Mappings;
using PostService.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IPostingService, PostingService>();
var app = builder.Build();
app.UseMiddleware<BusinessExceptionMiddleware>();

app.MapGet("/", () => "Це API поштового клієнта.");

app.MapGet("/postings", (IPostingService service) =>
    Results.Ok(service.GetAll().Select(PostingMapper.ToPostingGetDto).ToArray()));

app.MapGet("/postings/{id:int}", (int id, IPostingService service) =>
{
    var posting = service.Find(id);
    return posting is null ? Results.NotFound() : Results.Ok(PostingMapper.ToPostingGetDto(posting));
});

app.MapPost("/postings", (PostingPostDto dto, IPostingService service) =>
{
    var model = PostingMapper.ToPosting(dto);
    var saved = service.Create(model);
    var result = PostingMapper.ToPostingGetDto(saved);
    return Results.Created($"/postings/{result.Id}", result);
});

app.MapPut("/postings/{id:int}", (int id, PostingPutDto dto, IPostingService service) =>
{
    var updated = service.Update(id, PostingMapper.ToPosting(dto));
    return updated is null ? Results.NotFound() : Results.Ok(PostingMapper.ToPostingGetDto(updated));
});

app.MapDelete("/postings/{id:int}", (int id, IPostingService service) =>
    service.Delete(id) == 0 ? Results.NotFound() : Results.NoContent());

app.Run();
