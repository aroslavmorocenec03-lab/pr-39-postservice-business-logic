using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PostService.Dtos;
using PostService.Mappings;
using PostService.Models;
using PostService.Services;

Console.OutputEncoding = Encoding.UTF8;
var checks = new List<(string Name, Func<Task> Run)>
{
    ("Сервіс: три тарифи для 2 кг", () => Check(() =>
    {
        var service = new PostingService();
        Equal(60f, service.Create(Model(DeliveryType.Department)).Price);
        Equal(110f, service.Create(Model(DeliveryType.Courier)).Price);
        Equal(168f, service.Create(Model(DeliveryType.ExpressCourier)).Price);
    })),
    ("Сервіс: дробова вага враховується без округлення до кілограмів", () => Check(() =>
    {
        var posting = Model(); posting.Weight = 0.25f;
        Equal(42.5f, new PostingService().Create(posting).Price);
    })),
    ("Сервіс: Id, CreatedAt та Price визначає Create", () => Check(() =>
    {
        var input = Model(); input.Id = 999; input.CreatedAt = new DateTime(1900, 1, 1); input.Price = -999;
        var saved = new PostingService().Create(input);
        Equal(1, saved.Id); Equal(60f, saved.Price); Equal(DateTimeKind.Utc, saved.CreatedAt.Kind);
        if (DateTime.UtcNow - saved.CreatedAt > TimeSpan.FromMinutes(1)) throw new Exception("Непоточний час.");
    })),
    ("Сервіс: Max(Id)+1, порожній список та Delete", () => Check(() =>
    {
        var service = new PostingService();
        Equal(1, service.Create(Model()).Id); Equal(2, service.Create(Model()).Id);
        Equal(1, service.Delete(1)); Equal(3, service.Create(Model()).Id);
        Equal(1, service.Delete(2)); Equal(1, service.Delete(3));
        Equal(1, service.Create(Model()).Id);
    })),
    ("DI: один екземпляр у scope, різні у різних scope, спільні дані", () => Check(() =>
    {
        var services = new ServiceCollection(); services.AddScoped<IPostingService, PostingService>();
        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope(); using var scope2 = provider.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<IPostingService>();
        var aAgain = scope1.ServiceProvider.GetRequiredService<IPostingService>();
        var b = scope2.ServiceProvider.GetRequiredService<IPostingService>();
        Equal(true, ReferenceEquals(a, aAgain)); Equal(false, ReferenceEquals(a, b));
        var saved = a.Create(Model()); Equal(saved.Id, b.Find(saved.Id)!.Id);
    })),
    ("Мапер: вхідні та вихідний DTO з усіма потрібними даними", () => Check(() =>
    {
        var dto = Input(); var model = PostingMapper.ToPosting(dto);
        Equal(dto.From, model.From); Equal(dto.To, model.To); Equal(dto.Content, model.Content);
        Equal(dto.Weight, model.Weight); Equal(dto.Width, model.Width); Equal(dto.Height, model.Height);
        Equal(dto.Depth, model.Depth); Equal(dto.Value, model.Value); Equal(dto.DeliveryType, model.DeliveryType);
        var saved = new PostingService().Create(model); var result = PostingMapper.ToPostingGetDto(saved);
        Equal(saved.Id, result.Id); Equal(saved.CreatedAt, result.CreatedAt); Equal(60f, result.Price);
        Equal("A", result.From); Equal("B", result.To); Equal("Books", result.Content);
    })),
    ("Update: зміни зберігаються, час створення незмінний", () => Check(() =>
    {
        var service = new PostingService(); var saved = service.Create(Model());
        var changed = Model(); changed.Id = saved.Id; changed.Content = "Updated"; changed.Price = 77.5f;
        changed.CreatedAt = new DateTime(1900, 1, 1);
        var result = service.Update(saved.Id, changed)!;
        Equal("Updated", service.Find(saved.Id)!.Content); Equal(77.5f, result.Price);
        Equal(saved.CreatedAt, result.CreatedAt);
        var mismatch = Model(); mismatch.Id = 999;
        Throws<PostingValidationException>(() => service.Update(saved.Id, mismatch));
        Equal("Updated", service.Find(saved.Id)!.Content);
    })),
    ("Відсутні записи: Find/Update повертають null, Delete — 0", () => Check(() =>
    {
        var service = new PostingService();
        Equal<Posting?>(null, service.Find(999)); Equal<Posting?>(null, service.Update(999, Model())); Equal(0, service.Delete(999));
    })),
    ("Валідація бізнес-правил не додає некоректні записи", () => Check(() =>
    {
        var service = new PostingService();
        var bad = Model(); bad.Weight = 0; Throws<PostingValidationException>(() => service.Create(bad));
        bad = Model(); bad.DeliveryType = (DeliveryType)99; Throws<PostingValidationException>(() => service.Create(bad));
        bad = Model(); bad.From = ""; Throws<PostingValidationException>(() => service.Create(bad));
        Equal(0, service.GetAll().Count);
    })),
    ("Зовнішні зміни моделей і списків не пошкоджують сховище", () => Check(() =>
    {
        var service = new PostingService(); var input = Model(); var saved = service.Create(input);
        input.From = "Changed input"; saved.Price = -1;
        var list = service.GetAll(); list[0].Content = "Changed copy"; list.Clear();
        Equal("A", service.Find(1)!.From); Equal("Books", service.Find(1)!.Content); Equal(60f, service.Find(1)!.Price);
    }))
};

if (args.Length == 2 && args[0] == "--base-url")
{
    using var http = new HttpClient { BaseAddress = new Uri(args[1]) };
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); json.Converters.Add(new JsonStringEnumConverter());
    PostingGetDto? first = null;

    checks.Add(("HTTP POST: сервіс повертає автоматичні ціни 60/110/168", async () =>
    {
        foreach ((DeliveryType type, float price) in new[] { (DeliveryType.Department, 60f), (DeliveryType.Courier, 110f), (DeliveryType.ExpressCourier, 168f) })
        {
            var input = Input(); input.DeliveryType = type;
            using var response = await http.PostAsJsonAsync("postings", input, json);
            Equal(HttpStatusCode.Created, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<PostingGetDto>(json))!;
            Equal(price, result.Price); Equal("/postings/" + result.Id, response.Headers.Location!.ToString());
            first ??= result;
            Console.WriteLine($"  POST {type}, Weight=2 -> HTTP 201, Id={result.Id}, Price={result.Price}");
        }
    }));
    checks.Add(("HTTP POST: зайві Id/Price/CreatedAt не керують сервером", async () =>
    {
        using var response = await http.PostAsJsonAsync("postings", new { id = 999999, from = "A", to = "B", content = "Forged fields", deliveryType = "Department", weight = 2, width = 20, height = 15, depth = 2, value = 100, price = 0.01, createdAt = "1900-01-01T00:00:00Z" });
        Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PostingGetDto>(json))!;
        Equal(false, result.Id == 999999); Equal(60f, result.Price); Equal(true, result.CreatedAt.Year >= 2026);
    }));
    checks.Add(("HTTP GET: дані збережені між Scoped-запитами", async () =>
    {
        var result = (await http.GetFromJsonAsync<PostingGetDto[]> ("postings", json))!;
        Equal(4, result.Length);
        var one = (await http.GetFromJsonAsync<PostingGetDto>($"postings/{first!.Id}", json))!;
        Equal(60f, one.Price); Equal(first.CreatedAt, one.CreatedAt);
    }));
    checks.Add(("HTTP PUT: DTO редагування, збереження та незмінний CreatedAt", async () =>
    {
        var dto = Put(first!.Id); dto.Content = "Updated by PUT"; dto.Price = 77.5f;
        using var response = await http.PutAsJsonAsync($"postings/{first.Id}", dto, json);
        Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await http.GetFromJsonAsync<PostingGetDto>($"postings/{first.Id}", json))!;
        Equal("Updated by PUT", result.Content); Equal(77.5f, result.Price); Equal(first.CreatedAt, result.CreatedAt);
    }));
    checks.Add(("HTTP: неузгоджені Id перевіряє сервіс, відповідь 400", async () =>
    {
        using var response = await http.PutAsJsonAsync($"postings/{first!.Id}", Put(999), json);
        Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Equal(true, body.RootElement.GetProperty("errors").TryGetProperty("Id", out _));
    }));
    checks.Add(("HTTP: некоректна вага -> 400 з Errors.Weight", async () =>
    {
        var input = Input(); input.Weight = 0;
        using var response = await http.PostAsJsonAsync("postings", input, json);
        Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Equal(true, body.RootElement.GetProperty("errors").TryGetProperty("Weight", out _));
    }));
    checks.Add(("HTTP: 404 для GET, PUT і DELETE відсутнього запису", async () =>
    {
        using var get = await http.GetAsync("postings/999999"); Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var put = await http.PutAsJsonAsync("postings/999999", Put(999999), json); Equal(HttpStatusCode.NotFound, put.StatusCode);
        using var delete = await http.DeleteAsync("postings/999999"); Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }));
    checks.Add(("HTTP DELETE: 204 без тіла, потім 404", async () =>
    {
        using var deleted = await http.DeleteAsync($"postings/{first!.Id}"); Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Equal("", await deleted.Content.ReadAsStringAsync());
        using var missing = await http.GetAsync($"postings/{first.Id}"); Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }));

    int failed = 0;
    foreach (var check in checks)
    {
        Reset();
        try { await check.Run(); Console.WriteLine("PASS: " + check.Name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL: " + check.Name + " — " + error); }
    }
    Console.WriteLine($"\nУспішно: {checks.Count - failed}/{checks.Count}");
    return failed == 0 ? 0 : 1;
}

int failures = 0;
foreach (var check in checks)
{
    Reset();
    try { await check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL: " + check.Name + " — " + error); }
}
Console.WriteLine($"\nУспішно: {checks.Count - failures}/{checks.Count}");
return failures == 0 ? 0 : 1;

static Posting Model(DeliveryType type = DeliveryType.Department) => new() { From = "A", To = "B", Content = "Books", DeliveryType = type, Weight = 2, Width = 20, Height = 15, Depth = 2, Value = 100 };
static PostingPostDto Input() => new() { From = "A", To = "B", Content = "Books", DeliveryType = DeliveryType.Department, Weight = 2, Width = 20, Height = 15, Depth = 2, Value = 100 };
static PostingPutDto Put(int id) => new() { Id = id, From = "A", To = "B", Content = "Books", DeliveryType = DeliveryType.Department, Weight = 2, Width = 20, Height = 15, Depth = 2, Value = 100, Price = 60 };
static void Reset() { var service = new PostingService(); foreach (var item in service.GetAll()) service.Delete(item.Id); }
static Task Check(Action action) { action(); return Task.CompletedTask; }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Очікується {expected}, отримано {actual}."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Очікувався " + typeof(T).Name); }
