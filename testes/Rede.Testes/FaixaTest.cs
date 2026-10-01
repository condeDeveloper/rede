using Xunit;

namespace Conde.Rede.Testes;

/// <summary>
/// As faixas CIDR, e as invariantes que não dependem de implementação nenhuma.
/// </summary>
public class FaixaTest
{
    [Fact(DisplayName = "partir e juntar: as duas metades somam o pai, em todos os prefixos")]
    public void PartirEJuntar()
    {
        // É uma invariante, não uma comparação: partir uma faixa em duas e somar
        // os tamanhos tem de dar o tamanho original, e cada metade tem de caber
        // dentro do pai. Vale para os 32 prefixos de IPv4 que partem.
        var sorteio = new Random(29);

        for (var prefixo = 0; prefixo < 32; prefixo++)
        {
            for (var caso = 0; caso < 500; caso++)
            {
                var bytes = new byte[4];

                sorteio.NextBytes(bytes);

                var pai = new Faixa(Endereco.DeBytes(bytes), prefixo);

                var (primeira, segunda) = pai.Partir();

                Assert.Equal(pai.Quantos, primeira.Quantos + segunda.Quantos);

                Assert.True(pai.Contem(primeira));
                Assert.True(pai.Contem(segunda));

                // As duas metades não se cruzam.
                Assert.False(primeira.Contem(segunda));
                Assert.False(segunda.Contem(primeira));

                // E o pai de cada metade é o pai original.
                Assert.Equal(pai, primeira.Pai());
                Assert.Equal(pai, segunda.Pai());

                // A primeira começa onde o pai começa, e a segunda termina onde
                // o pai termina.
                Assert.Equal(pai.Base, primeira.Base);
                Assert.Equal(pai.Ultimo, segunda.Ultimo);
            }
        }
    }

    [Fact(DisplayName = "duas faixas ou são disjuntas ou uma contém a outra")]
    public void NuncaHaSobreposicaoParcial()
    {
        // Não existe sobreposição parcial entre faixas CIDR, e isso não é
        // coincidência: é consequência de o tamanho ser potência de dois e o
        // começo ser alinhado. É o que permite guardar rotas numa árvore em vez
        // de numa lista.
        var sorteio = new Random(7);

        var cruzaram = 0;

        for (var caso = 0; caso < 200_000; caso++)
        {
            var umBytes = new byte[4];
            var outroBytes = new byte[4];

            sorteio.NextBytes(umBytes);
            sorteio.NextBytes(outroBytes);

            // Metade das vezes, faixas próximas, para que o cruzamento aconteça.
            if (caso % 2 == 0)
            {
                umBytes.CopyTo(outroBytes, 0);

                outroBytes[3] = (byte)sorteio.Next(256);
            }

            var um = new Faixa(Endereco.DeBytes(umBytes), sorteio.Next(0, 33));
            var outro = new Faixa(Endereco.DeBytes(outroBytes), sorteio.Next(0, 33));

            var cruza = um.Cruza(outro);

            // A conta de verdade, feita pelos endereços: as faixas se tocam?
            var inicioUm = um.Base.ComoInteiro();
            var fimUm = um.Ultimo.ComoInteiro();
            var inicioOutro = outro.Base.ComoInteiro();
            var fimOutro = outro.Ultimo.ComoInteiro();

            var seTocam = inicioUm <= fimOutro && inicioOutro <= fimUm;

            Assert.True(cruza == seTocam,
                $"{um} e {outro}: Cruza diz {cruza} e os endereços dizem {seTocam}");

            if (cruza)
            {
                cruzaram++;

                // Se se tocam, uma contém a outra. Nunca parcial.
                Assert.True(um.Contem(outro) || outro.Contem(um),
                    $"{um} e {outro} se tocam sem uma conter a outra");
            }
        }

        Assert.True(cruzaram > 1_000, $"só {cruzaram} cruzamentos, o teste não exercitou nada");
    }

    [Theory(DisplayName = "as faixas conhecidas, com máscara e contagem")]
    [InlineData("192.168.0.0/16", "255.255.0.0", 65536, 65534, "192.168.255.255")]
    [InlineData("10.0.0.0/8", "255.0.0.0", 16777216, 16777214, "10.255.255.255")]
    [InlineData("172.16.0.0/12", "255.240.0.0", 1048576, 1048574, "172.31.255.255")]
    [InlineData("192.168.1.0/24", "255.255.255.0", 256, 254, "192.168.1.255")]
    [InlineData("203.0.113.0/30", "255.255.255.252", 4, 2, "203.0.113.3")]
    public void AsFaixasConhecidas(
        string texto, string mascara, long quantos, long usaveis, string ultimo)
    {
        var faixa = Faixa.Ler(texto);

        Assert.Equal(texto, faixa.ToString());
        Assert.Equal(mascara, faixa.Mascara().ToString());
        Assert.Equal(quantos, (long)faixa.Quantos);
        Assert.Equal(usaveis, (long)faixa.Usaveis());
        Assert.Equal(ultimo, faixa.Ultimo.ToString());
        Assert.Equal(ultimo, faixa.Broadcast()!.Value.ToString());

        Assert.True(faixa.Contem(faixa.Base));
        Assert.True(faixa.Contem(faixa.Ultimo));
    }

    [Fact(DisplayName = "o /31 e o /32, que a RFC 3021 trata diferente")]
    public void OTrintaEUmETrintaEDois()
    {
        // É a exceção que quase todo código erra: a conta "quantos menos dois"
        // vale até o /30, e daí para baixo não.
        var trintaEUm = Faixa.Ler("203.0.113.0/31");

        Assert.Equal(2, (long)trintaEUm.Quantos);

        // Os DOIS são usáveis. Numa ligação ponto a ponto não há broadcast a
        // fazer, então reservar dois de dois endereços deixaria zero.
        Assert.Equal(2, (long)trintaEUm.Usaveis());
        Assert.Null(trintaEUm.Broadcast());

        var trintaEDois = Faixa.Ler("203.0.113.5/32");

        Assert.Equal(1, (long)trintaEDois.Quantos);
        Assert.Equal(1, (long)trintaEDois.Usaveis());
        Assert.Null(trintaEDois.Broadcast());

        Assert.Equal("203.0.113.5", trintaEDois.Base.ToString());
        Assert.Equal("203.0.113.5", trintaEDois.Ultimo.ToString());
        Assert.True(trintaEDois.Contem(Endereco.Ler("203.0.113.5")));
        Assert.False(trintaEDois.Contem(Endereco.Ler("203.0.113.6")));

        // E no IPv6 todos são usáveis, porque não existe broadcast.
        var seisSeis = Faixa.Ler("2001:db8::/64");

        Assert.Equal(seisSeis.Quantos, seisSeis.Usaveis());
        Assert.Null(seisSeis.Broadcast());
    }

    [Fact(DisplayName = "a faixa normaliza os bits de host")]
    public void Normaliza()
    {
        // 192.168.1.57/24 e 192.168.1.0/24 são a mesma faixa. Guardar a primeira
        // forma faria duas faixas iguais compararem como diferentes.
        var comHost = Faixa.Ler("192.168.1.57/24");

        Assert.Equal("192.168.1.0/24", comHost.ToString());
        Assert.Equal(Faixa.Ler("192.168.1.0/24"), comHost);
        Assert.Equal(Faixa.Ler("192.168.1.0/24").GetHashCode(), comHost.GetHashCode());

        var sorteio = new Random(11);

        for (var caso = 0; caso < 100_000; caso++)
        {
            var bytes = new byte[4];

            sorteio.NextBytes(bytes);

            var prefixo = sorteio.Next(0, 33);

            var faixa = new Faixa(Endereco.DeBytes(bytes), prefixo);

            // Montar de novo a partir da base normalizada dá a mesma faixa.
            Assert.Equal(faixa, new Faixa(faixa.Base, prefixo));

            // E todos os bits depois do prefixo estão zerados.
            for (var bit = prefixo; bit < 32; bit++)
            {
                Assert.Equal(0, faixa.Base.Bytes[bit / 8] & (1 << (7 - bit % 8)));
            }
        }
    }

    [Theory(DisplayName = "o que o leitor recusa")]
    // Endereço com zero à esquerda, que em octal seria outro endereço.
    [InlineData("010.0.0.1")]
    [InlineData("192.168.01.1")]
    [InlineData("0251.0376.0251.0376")]
    // Partes demais ou de menos.
    [InlineData("127.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3.")]
    [InlineData(".1.2.3")]
    // Fora de faixa.
    [InlineData("256.0.0.1")]
    [InlineData("1.2.3.999")]
    // Letras.
    [InlineData("a.b.c.d")]
    [InlineData("")]
    public void EnderecoRecusado(string texto)
    {
        Assert.False(Endereco.TentarLer(texto, out _), $"aceitou \"{texto}\"");

        Assert.Throws<FormatException>(() => Endereco.Ler(texto));
    }

    [Theory(DisplayName = "o que o leitor de faixa recusa")]
    [InlineData("192.168.0.0")]
    [InlineData("192.168.0.0/")]
    [InlineData("192.168.0.0/33")]
    [InlineData("192.168.0.0/-1")]
    [InlineData("192.168.0.0/024")]
    [InlineData("192.168.0.0/abc")]
    [InlineData("2001:db8::/129")]
    [InlineData("/24")]
    public void FaixaRecusada(string texto)
    {
        Assert.False(Faixa.TentarLer(texto, out _), $"aceitou \"{texto}\"");

        Assert.Throws<FormatException>(() => Faixa.Ler(texto));
    }

    [Fact(DisplayName = "o endereço de metadados da nuvem, escrito em octal")]
    public void OEnderecoDeMetadados()
    {
        // 169.254.169.254 é o endereço de metadados da AWS, do Google e do
        // Azure. Um filtro que o recuse pelo texto costuma deixar passar a forma
        // octal, porque o inet_aton do sistema aceita as duas e resolve igual.
        Assert.True(Endereco.TentarLer("169.254.169.254", out var normal));

        // A forma octal é RECUSADA aqui, em vez de virar o mesmo endereço.
        Assert.False(Endereco.TentarLer("0251.0376.0251.0376", out _));
        Assert.False(Endereco.TentarLer("0251.00376.0251.0376", out _));

        // E a faixa de link-local, que é onde ele mora.
        var linkLocal = Faixa.Ler("169.254.0.0/16");

        Assert.True(linkLocal.Contem(normal));
        Assert.Equal(65_536, (long)linkLocal.Quantos);
    }

    [Fact(DisplayName = "as bordas")]
    public void AsBordas()
    {
        // O /0 cobre tudo.
        var tudo = Faixa.Ler("0.0.0.0/0");

        Assert.Equal(4_294_967_296, (long)tudo.Quantos);
        Assert.Equal("255.255.255.255", tudo.Ultimo.ToString());
        Assert.Equal("0.0.0.0", tudo.Mascara().ToString());

        var sorteio = new Random(3);

        for (var i = 0; i < 10_000; i++)
        {
            var bytes = new byte[4];

            sorteio.NextBytes(bytes);

            Assert.True(tudo.Contem(Endereco.DeBytes(bytes)));
        }

        // Um /0 não tem pai e um /32 não parte.
        Assert.Throws<InvalidOperationException>(() => tudo.Pai());
        Assert.Throws<InvalidOperationException>(() => Faixa.Ler("1.2.3.4/32").Partir());

        // Prefixo fora da faixa.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Faixa(Endereco.Ler("1.2.3.4"), 33));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Faixa(Endereco.Ler("1.2.3.4"), -1));

        // Famílias diferentes não se contêm.
        Assert.False(Faixa.Ler("0.0.0.0/0").Contem(Endereco.Ler("2001:db8::1")));
        Assert.False(Faixa.Ler("::/0").Contem(Endereco.Ler("1.2.3.4")));

        // Bytes de tamanho errado.
        Assert.Throws<ArgumentException>(() => Endereco.DeBytes(new byte[5]));
        Assert.Throws<ArgumentException>(() => Endereco.DeBytes(new byte[8]));

        // Um IPv6 não cabe em 32 bits.
        Assert.Throws<InvalidOperationException>(() => Endereco.Ler("2001:db8::1").ComoInteiro());

        // Um IPv6 tem 2^128 endereços, que não cabe em long nenhum. É por isso
        // que a contagem é BigInteger.
        Assert.Equal(
            System.Numerics.BigInteger.Pow(2, 128),
            Faixa.Ler("::/0").Quantos);
    }
}
