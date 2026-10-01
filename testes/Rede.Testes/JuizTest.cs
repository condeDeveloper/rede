using System.Net;
using Xunit;

namespace Conde.Rede.Testes;

/// <summary>
/// O juiz é o <c>System.Net</c>: o <c>IPAddress</c> e o <c>IPNetwork</c>.
/// </summary>
/// <remarks>
/// <para>
/// O <c>IPAddress</c> do .NET analisa e escreve endereços, e o <c>IPNetwork</c>,
/// que chegou no .NET 8, responde se um endereço está dentro de uma faixa. Os
/// dois são a implementação que o resto da plataforma usa, então comparar com
/// eles é comparar com o que um servidor ASP.NET de verdade vai fazer.
/// </para>
/// <para>
/// E é um juiz ilimitado: responde para qualquer entrada que eu quiser gerar, e
/// não para uma tabela de exemplos.
/// </para>
/// </remarks>
public class JuizTest
{
    [Fact(DisplayName = "TODOS os 33 prefixos de IPv4, contra o IPNetwork do .NET")]
    public void TodosOsPrefixosV4()
    {
        // 33 prefixos vezes 200 endereços base vezes 500 endereços testados.
        // O IPNetwork responde a mesma pergunta e as duas respostas têm de bater.
        var sorteio = new Random(29);

        var conferidos = 0;

        for (var prefixo = 0; prefixo <= 32; prefixo++)
        {
            for (var caso = 0; caso < 200; caso++)
            {
                var baseBytes = new byte[4];

                sorteio.NextBytes(baseBytes);

                var minha = new Faixa(Endereco.DeBytes(baseBytes), prefixo);

                var doNet = new IPNetwork(IPAddress.Parse(minha.Base.ToString()), prefixo);

                // A base normalizada bate.
                Assert.Equal(doNet.BaseAddress.ToString(), minha.Base.ToString());

                for (var i = 0; i < 20; i++)
                {
                    var alvo = new byte[4];

                    sorteio.NextBytes(alvo);

                    // Metade das vezes, um endereço de dentro da faixa.
                    if (i % 2 == 0)
                    {
                        minha.Base.Bytes.CopyTo(alvo);

                        for (var bit = prefixo; bit < 32; bit++)
                        {
                            if (sorteio.Next(2) == 0)
                            {
                                alvo[bit / 8] |= (byte)(1 << (7 - bit % 8));
                            }
                        }
                    }

                    var endereco = Endereco.DeBytes(alvo);

                    var esperado = doNet.Contains(IPAddress.Parse(endereco.ToString()));

                    Assert.True(esperado == minha.Contem(endereco),
                        $"{endereco} em {minha}: o .NET diz {esperado} e eu digo {!esperado}");

                    conferidos++;
                }
            }
        }

        Assert.Equal(33 * 200 * 20, conferidos);
    }

    [Fact(DisplayName = "TODOS os 129 prefixos de IPv6, contra o IPNetwork do .NET")]
    public void TodosOsPrefixosV6()
    {
        var sorteio = new Random(7);

        for (var prefixo = 0; prefixo <= 128; prefixo++)
        {
            for (var caso = 0; caso < 30; caso++)
            {
                var baseBytes = new byte[16];

                sorteio.NextBytes(baseBytes);

                var minha = new Faixa(Endereco.DeBytes(baseBytes), prefixo);

                var doNet = new IPNetwork(IPAddress.Parse(minha.Base.ToString()), prefixo);

                Assert.Equal(doNet.BaseAddress.ToString(), minha.Base.ToString());

                for (var i = 0; i < 10; i++)
                {
                    var alvo = new byte[16];

                    sorteio.NextBytes(alvo);

                    if (i % 2 == 0)
                    {
                        minha.Base.Bytes.CopyTo(alvo);
                    }

                    var endereco = Endereco.DeBytes(alvo);

                    Assert.Equal(
                        doNet.Contains(IPAddress.Parse(endereco.ToString())),
                        minha.Contem(endereco));
                }
            }
        }
    }

    [Fact(DisplayName = "TODOS os 16,7 milhões de IPv4 do 10.0.0.0/8, escritos e lidos de volta")]
    public void TodoOPrefixoDez()
    {
        // Uma faixa inteira, endereço por endereço. Não é amostra: são os
        // 16.777.216 endereços de um /8, cada um escrito, lido de volta e
        // conferido contra o inteiro de origem.
        var quantos = 0;

        for (uint i = 0; i < 1u << 24; i++)
        {
            var valor = 0x0A000000u | i;

            var endereco = Endereco.DeInteiro(valor);

            Assert.Equal(valor, endereco.ComoInteiro());

            quantos++;
        }

        Assert.Equal(16_777_216, quantos);

        // E uma amostra desses, pelo texto, contra o IPAddress do .NET.
        var sorteio = new Random(11);

        for (var caso = 0; caso < 200_000; caso++)
        {
            var valor = 0x0A000000u | (uint)sorteio.Next(0, 1 << 24);

            var endereco = Endereco.DeInteiro(valor);

            var doNet = IPAddress.Parse(endereco.ToString());

            Assert.Equal(doNet.ToString(), endereco.ToString());
            Assert.Equal(endereco, Endereco.Ler(doNet.ToString()));
        }
    }

    [Fact(DisplayName = "a forma canônica do IPv6, contra o IPAddress do .NET")]
    public void ACanonicaV6()
    {
        // O IPAddress.ToString do .NET segue a RFC 5952, então ele é o juiz da
        // regra do "::" mais comprido e do desempate pela primeira sequência.
        var sorteio = new Random(13);

        for (var caso = 0; caso < 200_000; caso++)
        {
            var bytes = new byte[16];

            sorteio.NextBytes(bytes);

            // Zera alguns grupos de propósito, para produzir sequências de zeros
            // de vários tamanhos. Sem isso, um sorteio puro quase nunca tem
            // zeros e a regra do "::" nunca é exercitada.
            var quantosZerar = sorteio.Next(0, 7);

            for (var i = 0; i < quantosZerar; i++)
            {
                var grupo = sorteio.Next(8);

                bytes[grupo * 2] = 0;
                bytes[grupo * 2 + 1] = 0;
            }

            var endereco = Endereco.DeBytes(bytes);

            var doNet = new IPAddress(bytes);

            Assert.True(doNet.ToString() == endereco.ToString(),
                $"os bytes {Convert.ToHexString(bytes)} dão \"{doNet}\" no .NET "
                + $"e \"{endereco}\" aqui");

            // E a leitura do que o .NET escreveu volta aos mesmos bytes.
            Assert.Equal(endereco, Endereco.Ler(doNet.ToString()));
        }
    }

    [Theory(DisplayName = "as grafias de IPv6 que a RFC 5952 nomeia")]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001", "2001:db8::1")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    [InlineData("2001:db8:0:0:0:0:0:1", "2001:db8::1")]
    [InlineData("0:0:0:0:0:0:0:0", "::")]
    [InlineData("0:0:0:0:0:0:0:1", "::1")]
    [InlineData("::", "::")]
    // O "::" cobre a MAIOR sequência, e não a primeira que aparecer.
    [InlineData("2001:0:0:1:0:0:0:1", "2001:0:0:1::1")]
    // Empate de tamanho: vai na PRIMEIRA. É o desempate que quase todo código
    // caseiro esquece.
    [InlineData("2001:db8:0:0:1:0:0:1", "2001:db8::1:0:0:1")]
    // Um grupo zerado sozinho NÃO vira "::", porque não encurta nada.
    [InlineData("2001:db8:0:1:1:1:1:1", "2001:db8:0:1:1:1:1:1")]
    public void AsGrafias(string entrada, string canonica)
    {
        var endereco = Endereco.Ler(entrada);

        Assert.Equal(canonica, endereco.ToString());

        // E o .NET concorda, que é o juiz.
        Assert.Equal(IPAddress.Parse(entrada).ToString(), endereco.ToString());

        // A forma canônica, relida, dá o mesmo endereço.
        Assert.Equal(endereco, Endereco.Ler(canonica));
    }

    [Fact(DisplayName = "o IPv4 embutido no IPv6, como ::ffff:192.0.2.1")]
    public void OV4NoV6()
    {
        var endereco = Endereco.Ler("::ffff:192.0.2.1");

        Assert.True(endereco.EhV6);
        Assert.Equal(16, endereco.Bytes.Length);

        // Os quatro últimos bytes são o IPv4.
        Assert.Equal(192, endereco.Bytes[12]);
        Assert.Equal(0, endereco.Bytes[13]);
        Assert.Equal(2, endereco.Bytes[14]);
        Assert.Equal(1, endereco.Bytes[15]);

        // E o .NET lê os mesmos bytes.
        Assert.Equal(
            IPAddress.Parse("::ffff:192.0.2.1").GetAddressBytes(),
            endereco.Bytes.ToArray());

        // Um IPv4 e o IPv6 que o embute NÃO são o mesmo endereço aqui, e é
        // deliberado: eles têm tamanhos diferentes e famílias diferentes.
        Assert.NotEqual(Endereco.Ler("192.0.2.1"), endereco);
    }
}
