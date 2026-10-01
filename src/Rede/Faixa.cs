namespace Conde.Rede;

/// <summary>
/// Uma faixa CIDR: um endereço e quantos bits do começo são fixos.
/// </summary>
/// <remarks>
/// <para>
/// O CIDR é de 1993 e salvou a internet de acabar antes do IPv6 chegar. Antes
/// dele os endereços vinham em três tamanhos fixos: classe A com 16 milhões de
/// endereços, classe B com 65 mil e classe C com 256. Uma empresa com 300
/// máquinas não cabia numa classe C e recebia uma classe B inteira, jogando
/// fora 65 mil endereços.
/// </para>
/// <para>
/// A ideia do CIDR é deixar o tamanho ser qualquer potência de dois. A mesma
/// empresa recebe um <c>/23</c>, com 512 endereços, e o resto continua
/// disponível.
/// </para>
/// </remarks>
public readonly struct Faixa : IEquatable<Faixa>
{
    /// <summary>O primeiro endereço da faixa, com os bits de host zerados.</summary>
    public Endereco Base { get; }

    /// <summary>Quantos bits do começo são fixos.</summary>
    public int Prefixo { get; }

    /// <summary>Monta a faixa, zerando os bits de host.</summary>
    /// <remarks>
    /// Zerar os bits de host é deliberado: <c>192.168.1.57/24</c> e
    /// <c>192.168.1.0/24</c> são a mesma faixa, e guardar a primeira forma faria
    /// duas faixas iguais compararem como diferentes. Quem quiser o endereço
    /// original que o guarde separado.
    /// </remarks>
    public Faixa(Endereco endereco, int prefixo)
    {
        if (prefixo < 0 || prefixo > endereco.Bits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prefixo), prefixo,
                $"um endereço de {endereco.Bits} bits aceita prefixo de 0 a {endereco.Bits}");
        }

        Prefixo = prefixo;

        var bytes = endereco.Bytes.ToArray();

        for (var bit = prefixo; bit < endereco.Bits; bit++)
        {
            bytes[bit / 8] &= (byte)~(1 << (7 - bit % 8));
        }

        Base = Endereco.DeBytes(bytes);
    }

    /// <summary>Lê a forma <c>endereço/prefixo</c>.</summary>
    public static Faixa Ler(string texto) => TentarLer(texto, out var faixa)
        ? faixa
        : throw new FormatException($"não é uma faixa CIDR: \"{texto}\"");

    /// <summary>Lê a forma <c>endereço/prefixo</c>, devolvendo falso em vez de lançar.</summary>
    public static bool TentarLer(string? texto, out Faixa faixa)
    {
        faixa = default;

        if (texto is null)
        {
            return false;
        }

        var barra = texto.IndexOf('/');

        if (barra < 0)
        {
            return false;
        }

        if (!Endereco.TentarLer(texto[..barra], out var endereco))
        {
            return false;
        }

        var depois = texto[(barra + 1)..];

        // Sem zero à esquerda e sem sinal, pelo mesmo motivo do endereço.
        if (depois.Length == 0 || depois.Length > 3
            || (depois.Length > 1 && depois[0] == '0')
            || !depois.All(char.IsAsciiDigit))
        {
            return false;
        }

        var prefixo = int.Parse(depois);

        if (prefixo > endereco.Bits)
        {
            return false;
        }

        faixa = new Faixa(endereco, prefixo);

        return true;
    }

    /// <summary>Quantos endereços a faixa tem.</summary>
    /// <remarks>
    /// Um <c>/0</c> de IPv6 tem 2^128 endereços, que não cabe em nenhum inteiro
    /// da plataforma. Por isso o tipo é <c>System.Numerics.BigInteger</c>, e não
    /// um <c>long</c> que estouraria em silêncio.
    /// </remarks>
    public System.Numerics.BigInteger Quantos =>
        System.Numerics.BigInteger.One << (Base.Bits - Prefixo);

    /// <summary>O último endereço da faixa.</summary>
    public Endereco Ultimo
    {
        get
        {
            var bytes = Base.Bytes.ToArray();

            for (var bit = Prefixo; bit < Base.Bits; bit++)
            {
                bytes[bit / 8] |= (byte)(1 << (7 - bit % 8));
            }

            return Endereco.DeBytes(bytes);
        }
    }

    /// <summary>A máscara, para IPv4. <c>/24</c> dá <c>255.255.255.0</c>.</summary>
    public Endereco Mascara()
    {
        var bytes = new byte[Base.Bytes.Length];

        for (var bit = 0; bit < Prefixo; bit++)
        {
            bytes[bit / 8] |= (byte)(1 << (7 - bit % 8));
        }

        return Endereco.DeBytes(bytes);
    }

    /// <summary>O endereço está dentro da faixa?</summary>
    public bool Contem(Endereco endereco)
    {
        if (endereco.Bytes.Length != Base.Bytes.Length)
        {
            return false;
        }

        var inteiros = Prefixo / 8;

        for (var i = 0; i < inteiros; i++)
        {
            if (endereco.Bytes[i] != Base.Bytes[i])
            {
                return false;
            }
        }

        var sobra = Prefixo % 8;

        if (sobra == 0)
        {
            return true;
        }

        var mascara = (byte)(0xFF << (8 - sobra));

        return (endereco.Bytes[inteiros] & mascara) == (Base.Bytes[inteiros] & mascara);
    }

    /// <summary>A outra faixa cabe inteira dentro desta?</summary>
    public bool Contem(Faixa outra) =>
        outra.Prefixo >= Prefixo
        && outra.Base.Bytes.Length == Base.Bytes.Length
        && Contem(outra.Base);

    /// <summary>
    /// As duas faixas se sobrepõem?
    /// </summary>
    /// <remarks>
    /// Duas faixas CIDR ou são disjuntas ou uma contém a outra. Não existe
    /// sobreposição parcial, e isso não é coincidência: é consequência de o
    /// tamanho ser sempre potência de dois e o começo ser alinhado. É o que
    /// permite guardar rotas numa árvore em vez de numa lista.
    /// </remarks>
    public bool Cruza(Faixa outra) => Contem(outra) || outra.Contem(this);

    /// <summary>Parte a faixa em duas metades.</summary>
    public (Faixa Primeira, Faixa Segunda) Partir()
    {
        if (Prefixo >= Base.Bits)
        {
            throw new InvalidOperationException(
                $"um /{Prefixo} já é um endereço só e não parte em dois");
        }

        var bytes = Base.Bytes.ToArray();

        bytes[Prefixo / 8] |= (byte)(1 << (7 - Prefixo % 8));

        return (new Faixa(Base, Prefixo + 1),
                new Faixa(Endereco.DeBytes(bytes), Prefixo + 1));
    }

    /// <summary>A faixa que contém esta e a vizinha dela.</summary>
    public Faixa Pai()
    {
        if (Prefixo == 0)
        {
            throw new InvalidOperationException("um /0 já cobre tudo");
        }

        return new Faixa(Base, Prefixo - 1);
    }

    /// <summary>
    /// Os endereços usáveis para máquinas, numa faixa IPv4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Numa rede IPv4 comum, o primeiro endereço é a rede e o último é o
    /// broadcast, então um <c>/24</c> tem 256 endereços e 254 utilizáveis.
    /// </para>
    /// <para>
    /// As duas exceções estão na RFC 3021 e são as que quase todo código erra:
    /// um <c>/31</c> tem 2 endereços e os <b>dois</b> são usáveis, porque numa
    /// ligação ponto a ponto não há broadcast a fazer. E um <c>/32</c> é um
    /// endereço só, que é ele mesmo.
    /// </para>
    /// </remarks>
    public System.Numerics.BigInteger Usaveis()
    {
        if (!Base.EhV4)
        {
            // No IPv6 não há broadcast, então todos são usáveis.
            return Quantos;
        }

        return Prefixo >= 31 ? Quantos : Quantos - 2;
    }

    /// <summary>O endereço de broadcast, que só existe em IPv4 com prefixo até 30.</summary>
    public Endereco? Broadcast() => Base.EhV4 && Prefixo <= 30 ? Ultimo : null;

    public bool Equals(Faixa outra) => Prefixo == outra.Prefixo && Base.Equals(outra.Base);

    public override bool Equals(object? outro) => outro is Faixa faixa && Equals(faixa);

    public override int GetHashCode() => HashCode.Combine(Base, Prefixo);

    public static bool operator ==(Faixa um, Faixa outro) => um.Equals(outro);

    public static bool operator !=(Faixa um, Faixa outro) => !um.Equals(outro);

    public override string ToString() => $"{Base}/{Prefixo}";
}
