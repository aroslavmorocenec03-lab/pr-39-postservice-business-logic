# Практична робота №39

## Виокремлення бізнес-логіки в сервісний рівень

Поштовий Minimal API продовжено з ПР-37–38: доменну модель відокремлено від дата-контрактів, роботу зі списком перенесено в сервіс, а вартість нового відправлення обчислюється автоматично.

**[Звіт і відповіді на 10 контрольних запитань](reports/REPORT.md)**.

## Запуск

Потрібен **.NET SDK 10**. Завантажте через **Code → Download ZIP**, розпакуйте й відкрийте термінал у кореневій папці.

```powershell
dotnet build PostService/PostService.csproj
dotnet run --project PostService
```

Сервер працює на **`http://localhost:5050`**. Початковий список порожній; для створення скористайтеся першим POST у **[requests.http](requests.http)**. У VS Code використовуйте REST Client `humao.rest-client` і Send Request. Наступні запити підставляють Id із відповіді іменованого POST `createPosting`.

Дані існують лише в пам’яті процесу; перезапуск очищує список.

## Поділ відповідальності

| Рівень | Файл / папка | Відповідальність |
| --- | --- | --- |
| Доменна модель | `Models/Posting.cs` | Внутрішня сутність відправлення |
| Дата-контракти | `Dtos` | Окремі вхідні та вихідні формати HTTP |
| Мапер | `Mappings/PostingMapper.cs` | Перетворення DTO ↔ Posting, без тарифів і сховища |
| Сервіс | `Services/IPostingService.cs`, `PostingService.cs` | Create/GetAll/Find/Update/Delete, Id, час, валідація й ціна |
| HTTP | `Program.cs` | Прив’язування DTO, виклики мапера/сервісу, статус і тіло відповіді |
| Перетворення помилки | `Infrastructure/BusinessExceptionMiddleware.cs` | Перекладає виняток валідації сервісу в HTTP 400 |

`Program.cs` не містить списку, генерації Id, присвоєння `CreatedAt` або тарифних формул. Модель і DTO не викликають один одного. Перетворення зосереджені в статичному `PostingMapper`.

### DTO

- **PostingPostDto:** From, To, Content, DeliveryType, Weight, Width, Height, Depth, Value. **Price, Id і CreatedAt відсутні.**
- **PostingPutDto:** ті самі дані, а також Id і Price для контракту редагування. CreatedAt не передається.
- **PostingGetDto:** усі дані, включно із серверними Id, Price та CreatedAt.

Розміри в поточному контракті — `int`, як у прикладі ПР-39; вага, цінність і ціна — `float`. Проміжний розрахунок ціни виконується в `decimal`, округлюється до двох знаків і перетворюється в тип моделі.

## Автоматична ціна при створенні

**У документі конкретних тарифів не наведено.** Для навчальної реалізації взято тарифи з попередніх практичних, але застосовано їх до фактичної `Weight`, як вимагає ця робота:

| DeliveryType | Формула, грн | Для 2 кг |
| --- | --- | ---: |
| Department | `40 + 10 × Weight` | 60 |
| Courier | `80 + 15 × Weight` | 110 |
| ExpressCourier | `120 + 24 × Weight` | 168 |

Формули розміщено **лише в PostingService.CalculatePrice**. Дробова вага враховується без округлення до цілих кілограмів. Габарити не беруть участі в цій тарифній формулі. Тарифи є явним припущенням реалізації, а не цитатою з документа.

POST-обробник має універсальний вигляд, який не залежить від способу обчислення ціни:

```csharp
app.MapPost("/postings", (PostingPostDto dto, IPostingService service) =>
{
    var model = PostingMapper.ToPosting(dto);
    var saved = service.Create(model);
    var result = PostingMapper.ToPostingGetDto(saved);
    return Results.Created($"/postings/{result.Id}", result);
});
```

Приклад тіла POST:

```json
{
  "from": "Ivan",
  "to": "Olena",
  "content": "Documents",
  "deliveryType": "Department",
  "weight": 2,
  "width": 20,
  "height": 15,
  "depth": 2,
  "value": 100
}
```

Відповідь має статус **201**, `Location` та, зокрема, **`"price": 60`**. Повний фактичний запит із `requests.http` і відповідь: [reports/post-response.txt](reports/post-response.txt).

Правило автоматичної ціни за завданням застосовується **під час створення**. У PUT залишено Price як окреме поле контракту редагування; сервіс перевіряє його невід’ємність. CreatedAt при оновленні зберігається.

## Сервіс і DI

До `builder.Build()` виконано:

```csharp
builder.Services.AddScoped<IPostingService, PostingService>();
```

Обробники отримують `IPostingService` параметром. Контейнер створює Scoped-екземпляр для запиту; `new PostingService()` у маршрутах не використовується.

Сховище — статичний `List<Posting>` у сервісі, спільний між його Scoped-екземплярами. Саме `static`, а не час життя Scoped, забезпечує збереження даних між запитами. Статичний lock захищає одночасний доступ. Сервіс повертає копії моделей, щоб зовнішня зміна об’єкта не змінила сховище в обхід бізнес-правил.

Create присвоює `Max(Id) + 1`, або 1 для порожнього списку, та поточний UTC-час. Така навчальна схема гарантує унікальність серед наявних записів, але може знову використати Id після видалення максимального запису чи очищення списку.

Update отримує **Id зі шляху та модель із DTO**: `Update(int postingId, Posting posting)`. Додатковий параметр дозволяє перевірити збіг Id у самому сервісі, залишаючи HTTP-обробник без цієї перевірки. Find/Update повертають null для відсутнього запису; Delete — кількість видалених записів.

Правила даних також розташовані у сервісі: обов’язкові From/To/Content, відомий спосіб доставки, вага понад 0 до 1000000, додатні розміри, невід’ємна цінність та, для PUT, ціна. Порушення передаються словником у `PostingValidationException`; middleware формує відповідь 400 з `errors`.

## Маршрути

| Метод | Шлях | Результат |
| --- | --- | --- |
| GET | `/postings` | 200, масив PostingGetDto |
| GET | `/postings/{id}` | 200 з DTO або 404 |
| POST | `/postings` | 201 з DTO, серверною ціною та Location |
| PUT | `/postings/{id}` | 200 з DTO, 404 або 400 |
| DELETE | `/postings/{id}` | 204 або 404 |

## Перевірки

Без сервера — 10 перевірок сервісу, мапера та DI:

```powershell
dotnet run --project Tests/ServiceChecks.csproj
```

Для повного набору запустіть **новий екземпляр сервера з порожнім списком**, а в іншому терміналі:

```powershell
dotnet run --project Tests/ServiceChecks.csproj -- --base-url http://localhost:5050/
```

Підтверджено **18/18 перевірок**: три тарифи, дробову вагу, серверні поля, генерацію Id, Scoped-екземпляри, мапер, оновлення, відсутні записи, валідацію, ізоляцію копій, реальні CRUD-запити, 400, 404 і 204. Окремий HTTP-тест передає зайві `price`, `id`, `createdAt`: DTO їх не має, і вони не визначають результат Create.

Фактичний протокол: [reports/checks-output.txt](reports/checks-output.txt). REST Client-файл додано; стан його перевірки у VS Code описано у [звіті](reports/REPORT.md).
