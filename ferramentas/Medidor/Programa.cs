using System.Diagnostics;
using System.Net;
using Conde.Rede;

namespace Conde.Rede.Ferramentas.Medidor;

/// <summary>
/// A ferramenta: mostra as faixas e registra os números.
/// </summary>
/// <remarks>
/// Tudo que depende de relógio mora aqui, e não na suíte de testes. Uma
/// afirmação de tempo falha sem que nada tenha piorado, basta a máquina do CI
/// estar ocupada. Esta ferramenta imprime e sai com zero, sempre.
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        // Procura o comando entre TODOS os argumentos. O `dotnet run` nem sempre
        // entrega so o que vem depois do `--`.
        var comandos = new[] { "faixa", "canonica", "octal", "privadas", "velocidade" };

        var onde = Array.FindIndex(argumentos, a => comandos.Contains(a));

        if (onde < 0)
        {
            Console.WriteLine(Ajuda);

            return 0;
        }

        var resto = argumentos[onde..];

        switch (resto[0])
        {
            case "faixa":
                AFaixa(resto.Length > 1 ? resto[1] : "192.168.1.0/24");
                break;

            case "canonica":
                Canonica();
                break;

            case "octal":
                Octal();
                break;

            case "privadas":
                Privadas();
                break;

            default:
                Velocidade();
                break;
        }

        return 0;
    }

    /// <summary>Uma faixa, aberta.</summary>
    private static void AFaixa(string texto)
    {
        if (!Faixa.TentarLer(texto, out var faixa))
        {
            Console.WriteLine($"nao e uma faixa CIDR: {texto}");

            return;
        }

        Console.WriteLine($"{faixa}");
        Console.WriteLine();
        Console.WriteLine($"  rede          {faixa.Base}");
        Console.WriteLine($"  mascara       {faixa.Mascara()}");
        Console.WriteLine($"  ultimo        {faixa.Ultimo}");
        Console.WriteLine($"  broadcast     {(faixa.Broadcast()?.ToString() ?? "nao tem")}");
        Console.WriteLine($"  enderecos     {faixa.Quantos:N0}");
        Console.WriteLine($"  usaveis       {faixa.Usaveis():N0}");
        Console.WriteLine();

        if (faixa.Prefixo < faixa.Base.Bits)
        {
            var (primeira, segunda) = faixa.Partir();

            Console.WriteLine($"  partida em    {primeira}  e  {segunda}");
        }

        if (faixa.Prefixo > 0)
        {
            Console.WriteLine($"  dentro de     {faixa.Pai()}");
        }
    }

    /// <summary>As grafias de IPv6 que viram a mesma coisa.</summary>
    private static void Canonica()
    {
        Console.WriteLine("Um endereco IPv6, varias grafias, uma forma canonica");
        Console.WriteLine();
        Console.WriteLine("  como veio                                 canonica          igual ao .NET?");
        Console.WriteLine("  " + new string('-', 76));

        string[] grafias =
        [
            "2001:0db8:0000:0000:0000:0000:0000:0001",
            "2001:DB8::1",
            "2001:db8:0:0:0:0:0:1",
            "2001:db8::1",
            "0:0:0:0:0:0:0:0",
            "::1",
            "2001:0:0:1:0:0:0:1",
            "2001:db8:0:0:1:0:0:1",
            "2001:db8:0:1:1:1:1:1",
            "::ffff:192.0.2.1",
            "fe80::1%0",
        ];

        foreach (var texto in grafias)
        {
            if (!Endereco.TentarLer(texto, out var endereco))
            {
                Console.WriteLine($"  {texto,-40}  (recusado)");

                continue;
            }

            var doNet = IPAddress.Parse(texto).ToString();

            Console.WriteLine(
                $"  {texto,-40}  {endereco,-18} {(doNet == endereco.ToString() ? "sim" : "NAO: " + doNet)}");
        }

        Console.WriteLine();
        Console.WriteLine("  Sem a RFC 5952, as quatro primeiras linhas sao quatro enderecos");
        Console.WriteLine("  diferentes para quem compara texto. Numa lista de bloqueio ou num");
        Console.WriteLine("  registro de acesso, isso e um buraco.");
        Console.WriteLine();
        Console.WriteLine("  As duas regras que quase todo codigo caseiro erra: o :: cobre a MAIOR");
        Console.WriteLine("  sequencia de zeros, e no empate vai na PRIMEIRA. E um grupo zerado");
        Console.WriteLine("  sozinho nao vira ::, porque nao encurta nada.");
    }

    /// <summary>O endereço em octal que fura filtro.</summary>
    private static void Octal()
    {
        Console.WriteLine("O mesmo endereco, escrito de jeitos que o sistema aceita");
        Console.WriteLine();

        var alvo = "169.254.169.254";

        Console.WriteLine($"  O alvo e {alvo}, o endereco de metadados da AWS, do Google e");
        Console.WriteLine("  do Azure. Um filtro que o recuse pelo texto precisa pegar tambem:");
        Console.WriteLine();
        Console.WriteLine("    como escrito                  o inet_aton do C   aqui");
        Console.WriteLine("  " + new string('-', 64));

        (string Texto, string OQueOCFaz)[] formas =
        [
            ("169.254.169.254", "169.254.169.254"),
            ("0251.0376.0251.0376", "169.254.169.254 (octal)"),
            ("0xA9.0xFE.0xA9.0xFE", "169.254.169.254 (hexa)"),
            ("2852039166", "169.254.169.254 (um numero so)"),
            ("169.16689662", "169.254.169.254 (duas partes)"),
        ];

        foreach (var (texto, oQueOCFaz) in formas)
        {
            var aceito = Endereco.TentarLer(texto, out var endereco);

            Console.WriteLine(
                $"    {texto,-28}  {oQueOCFaz,-18} {(aceito ? endereco.ToString() : "recusado")}");
        }

        Console.WriteLine();
        Console.WriteLine("  Este leitor aceita so a forma de quatro numeros decimais, sem zero a");
        Console.WriteLine("  esquerda. Todas as outras sao recusadas, e e deliberado: aceitar as");
        Console.WriteLine("  duas leituras de 0251 e como um filtro deixa passar o que devia barrar.");
    }

    /// <summary>As faixas reservadas.</summary>
    private static void Privadas()
    {
        Console.WriteLine("As faixas reservadas, e quanto cada uma vale");
        Console.WriteLine();
        Console.WriteLine("  faixa                 enderecos        usaveis   para que serve");
        Console.WriteLine("  " + new string('-', 76));

        (string Cidr, string OQue)[] faixas =
        [
            ("10.0.0.0/8", "privada, RFC 1918"),
            ("172.16.0.0/12", "privada, RFC 1918"),
            ("192.168.0.0/16", "privada, RFC 1918"),
            ("127.0.0.0/8", "laco local"),
            ("169.254.0.0/16", "link-local, e a metadados da nuvem"),
            ("100.64.0.0/10", "CGNAT, RFC 6598"),
            ("224.0.0.0/4", "multicast"),
            ("192.0.2.0/24", "documentacao, RFC 5737"),
            ("198.51.100.0/24", "documentacao, RFC 5737"),
            ("203.0.113.0/24", "documentacao, RFC 5737"),
        ];

        System.Numerics.BigInteger total = 0;

        foreach (var (cidr, oQue) in faixas)
        {
            var faixa = Faixa.Ler(cidr);

            total += faixa.Quantos;

            Console.WriteLine(
                $"  {cidr,-20} {faixa.Quantos,12:N0} {faixa.Usaveis(),14:N0}   {oQue}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Somando: {total:N0} enderecos de 4.294.967.296, "
            + $"ou {(double)total / 4_294_967_296 * 100:F1}%");
        Console.WriteLine();
        Console.WriteLine("  As tres faixas de documentacao existem para que exemplo em livro e em");
        Console.WriteLine("  manual nao aponte para a maquina de ninguem. Sao as unicas que podem");
        Console.WriteLine("  aparecer num texto publicado sem incomodar alguem.");
    }

    /// <summary>A velocidade, contra o System.Net.</summary>
    private static void Velocidade()
    {
        Console.WriteLine("A velocidade, contra o System.Net do .NET");
        Console.WriteLine();

        const int quantos = 1_000_000;

        var sorteio = new Random(29);

        var textos = new string[quantos];

        for (var i = 0; i < quantos; i++)
        {
            textos[i] = $"{sorteio.Next(1, 256)}.{sorteio.Next(256)}.{sorteio.Next(256)}.{sorteio.Next(1, 256)}";
        }

        var relogio = Stopwatch.StartNew();

        var soma = 0L;

        foreach (var texto in textos)
        {
            soma += Endereco.Ler(texto).Bytes[0];
        }

        var meuLer = relogio.Elapsed.TotalMilliseconds;

        relogio.Restart();

        foreach (var texto in textos)
        {
            soma += IPAddress.Parse(texto).GetAddressBytes()[0];
        }

        var doNetLer = relogio.Elapsed.TotalMilliseconds;

        var faixa = Faixa.Ler("10.0.0.0/8");
        var doNetFaixa = new IPNetwork(IPAddress.Parse("10.0.0.0"), 8);

        var enderecos = textos.Select(Endereco.Ler).ToArray();
        var doNetEnderecos = textos.Select(IPAddress.Parse).ToArray();

        relogio.Restart();

        var dentro = 0;

        foreach (var endereco in enderecos)
        {
            if (faixa.Contem(endereco))
            {
                dentro++;
            }
        }

        var meuContem = relogio.Elapsed.TotalMilliseconds;

        relogio.Restart();

        foreach (var endereco in doNetEnderecos)
        {
            if (doNetFaixa.Contains(endereco))
            {
                dentro++;
            }
        }

        var doNetContem = relogio.Elapsed.TotalMilliseconds;

        Console.WriteLine("  operacao                      meu       .NET     razao");
        Console.WriteLine("  " + new string('-', 56));
        Console.WriteLine($"  ler o texto              {meuLer,8:F1} ms {doNetLer,8:F1} ms {meuLer / doNetLer,7:F1}x");
        Console.WriteLine($"  esta na faixa?           {meuContem,8:F1} ms {doNetContem,8:F1} ms {meuContem / doNetContem,7:F1}x");
        Console.WriteLine();
        Console.WriteLine($"  ({quantos:N0} enderecos; {dentro:N0} dentro do 10.0.0.0/8, controle {soma})");
        Console.WriteLine();
        Console.WriteLine("  A comparacao de faixa e barata dos dois lados: sao alguns bytes e uma");
        Console.WriteLine("  mascara. O que custa e a leitura do texto, e e por isso que um filtro");
        Console.WriteLine("  de verdade guarda os enderecos ja analisados.");
    }

    private const string Ajuda = """
        rede -- enderecos IP e faixas CIDR do zero

          medidor faixa [CIDR]   uma faixa, aberta
          medidor canonica       as grafias de IPv6 que viram a mesma coisa
          medidor octal          o endereco em octal que fura filtro
          medidor privadas       as faixas reservadas, e quanto cada uma vale
          medidor velocidade     contra o System.Net
        """;
}
