using System.Buffers.Binary;
using System.Text;

namespace Conde.Rede;

/// <summary>
/// Um endereço IP, de 4 ou de 16 bytes.
/// </summary>
/// <remarks>
/// <para>
/// A parte fácil é guardar os bytes. A difícil é a forma escrita do IPv6, que
/// tem várias grafias para o mesmo endereço, e por isso tem uma norma só para
/// escolher qual delas usar: a RFC 5952.
/// </para>
/// <para>
/// Sem essa norma, <c>2001:db8::1</c>, <c>2001:0db8::1</c>,
/// <c>2001:db8:0:0:0:0:0:1</c> e <c>2001:DB8::1</c> são o mesmo endereço escrito
/// de quatro jeitos. Um sistema que compare endereços como texto, numa lista de
/// bloqueio ou num registro de acesso, trata os quatro como coisas diferentes.
/// </para>
/// </remarks>
public readonly struct Endereco : IEquatable<Endereco>, IComparable<Endereco>
{
    private readonly byte[] _bytes;

    /// <summary>Os bytes, 4 para IPv4 e 16 para IPv6.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes ?? ZeroV4;

    private static readonly byte[] ZeroV4 = new byte[4];

    private Endereco(byte[] bytes) => _bytes = bytes;

    /// <summary>Quantos bits o endereço tem: 32 ou 128.</summary>
    public int Bits => Bytes.Length * 8;

    /// <summary>É IPv4?</summary>
    public bool EhV4 => Bytes.Length == 4;

    /// <summary>É IPv6?</summary>
    public bool EhV6 => Bytes.Length == 16;

    /// <summary>Monta a partir dos bytes.</summary>
    public static Endereco DeBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not (4 or 16))
        {
            throw new ArgumentException(
                $"um endereço tem 4 ou 16 bytes, e vieram {bytes.Length}", nameof(bytes));
        }

        return new Endereco(bytes.ToArray());
    }

    /// <summary>Monta um IPv4 a partir do inteiro de 32 bits.</summary>
    public static Endereco DeInteiro(uint valor)
    {
        var bytes = new byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(bytes, valor);

        return new Endereco(bytes);
    }

    /// <summary>O IPv4 como inteiro de 32 bits.</summary>
    public uint ComoInteiro()
    {
        if (!EhV4)
        {
            throw new InvalidOperationException("só um IPv4 cabe em 32 bits");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(Bytes);
    }

    /// <summary>Lê um endereço, IPv4 ou IPv6.</summary>
    public static Endereco Ler(string texto) => TentarLer(texto, out var endereco)
        ? endereco
        : throw new FormatException($"não é um endereço IP: \"{texto}\"");

    /// <summary>Lê um endereço, devolvendo falso em vez de lançar.</summary>
    /// <remarks>
    /// <para>
    /// O IPv4 aqui aceita só a forma de quatro números decimais. Isso é mais
    /// estrito que o <c>inet_aton</c> do C, que aceita octal, hexadecimal e
    /// menos de quatro partes: para ele, <c>127.1</c> é 127.0.0.1 e
    /// <c>0177.0.0.1</c> também.
    /// </para>
    /// <para>
    /// Essa tolerância é uma fonte conhecida de contrabando de requisição. Um
    /// filtro que recusa <c>169.254.169.254</c>, o endereço de metadados da
    /// nuvem, costuma deixar passar <c>0251.0376.0251.0376</c>, que é o mesmo
    /// endereço em octal, e o sistema operacional resolve os dois igual. Um
    /// zero à esquerda é recusado aqui de propósito.
    /// </para>
    /// </remarks>
    public static bool TentarLer(string? texto, out Endereco endereco)
    {
        endereco = default;

        if (string.IsNullOrEmpty(texto))
        {
            return false;
        }

        return texto.Contains(':', StringComparison.Ordinal)
            ? TentarLerV6(texto, out endereco)
            : TentarLerV4(texto, out endereco);
    }

    private static bool TentarLerV4(string texto, out Endereco endereco)
    {
        endereco = default;

        var bytes = new byte[4];

        var parte = 0;
        var valor = 0;
        var algarismos = 0;

        foreach (var letra in texto)
        {
            if (letra == '.')
            {
                if (algarismos == 0 || parte == 3)
                {
                    return false;
                }

                bytes[parte++] = (byte)valor;

                valor = 0;
                algarismos = 0;

                continue;
            }

            if (letra is < '0' or > '9')
            {
                return false;
            }

            // Zero à esquerda é recusado: em octal ele mudaria o valor, e aceitar
            // as duas leituras é como um filtro deixa passar o que devia barrar.
            if (algarismos == 1 && valor == 0)
            {
                return false;
            }

            valor = valor * 10 + (letra - '0');
            algarismos++;

            if (algarismos > 3 || valor > 255)
            {
                return false;
            }
        }

        if (algarismos == 0 || parte != 3)
        {
            return false;
        }

        bytes[3] = (byte)valor;

        endereco = new Endereco(bytes);

        return true;
    }

    private static bool TentarLerV6(string texto, out Endereco endereco)
    {
        endereco = default;

        var bytes = new byte[16];

        var bruto = texto.AsSpan();

        // A forma com IPv4 no fim, como ::ffff:192.0.2.1.
        var ultimoDoisPontos = bruto.LastIndexOf(':');

        var comV4 = bruto[(ultimoDoisPontos + 1)..].Contains('.');

        byte[]? finalV4 = null;

        if (comV4)
        {
            if (!TentarLerV4(bruto[(ultimoDoisPontos + 1)..].ToString(), out var quatro))
            {
                return false;
            }

            finalV4 = quatro.Bytes.ToArray();

            bruto = bruto[..(ultimoDoisPontos + 1)];
        }

        var grupos = new List<ushort>();

        var ondeOSalto = -1;

        var onde = 0;

        if (bruto.StartsWith("::"))
        {
            ondeOSalto = 0;
            onde = 2;
        }
        else if (bruto.StartsWith(":"))
        {
            return false;
        }

        while (onde < bruto.Length)
        {
            if (bruto[onde] == ':')
            {
                if (ondeOSalto >= 0)
                {
                    return false;   // dois saltos, que seria ambíguo
                }

                ondeOSalto = grupos.Count;
                onde++;

                continue;
            }

            var valor = 0;
            var algarismos = 0;

            while (onde < bruto.Length && bruto[onde] != ':')
            {
                var digito = Hexa(bruto[onde]);

                if (digito < 0 || ++algarismos > 4)
                {
                    return false;
                }

                valor = valor * 16 + digito;
                onde++;
            }

            if (algarismos == 0)
            {
                return false;
            }

            grupos.Add((ushort)valor);

            if (onde < bruto.Length)
            {
                onde++;   // pula o ':'

                if (onde == bruto.Length && !comV4)
                {
                    return false;   // termina em ':' solto
                }
            }
        }

        var quantosGrupos = finalV4 is null ? 8 : 6;

        if (ondeOSalto < 0)
        {
            if (grupos.Count != quantosGrupos)
            {
                return false;
            }
        }
        else if (grupos.Count >= quantosGrupos)
        {
            return false;   // o salto tem de cobrir pelo menos um grupo
        }

        var saida = new ushort[quantosGrupos];

        for (var i = 0; i < ondeOSalto || (ondeOSalto < 0 && i < grupos.Count); i++)
        {
            saida[i] = grupos[i];
        }

        if (ondeOSalto >= 0)
        {
            var depois = grupos.Count - ondeOSalto;

            for (var i = 0; i < depois; i++)
            {
                saida[quantosGrupos - depois + i] = grupos[ondeOSalto + i];
            }
        }

        for (var i = 0; i < quantosGrupos; i++)
        {
            bytes[i * 2] = (byte)(saida[i] >> 8);
            bytes[i * 2 + 1] = (byte)saida[i];
        }

        finalV4?.CopyTo(bytes, 12);

        endereco = new Endereco(bytes);

        return true;
    }

    private static int Hexa(char letra) => letra switch
    {
        >= '0' and <= '9' => letra - '0',
        >= 'a' and <= 'f' => letra - 'a' + 10,
        >= 'A' and <= 'F' => letra - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// A forma canônica da RFC 5952.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A norma tem quatro regras, e as duas últimas são as que quase toda
    /// implementação caseira erra:
    /// </para>
    /// <list type="number">
    ///   <item><description>sem zeros à esquerda em cada grupo</description></item>
    ///   <item><description>letras em minúsculas</description></item>
    ///   <item><description>o <c>::</c> cobre a <b>maior</b> sequência de grupos zerados</description></item>
    ///   <item><description>o <c>::</c> <b>não</b> é usado para um grupo só, porque não encurta nada</description></item>
    /// </list>
    /// <para>
    /// A regra 3 tem um desempate que falta em quase todo código: quando há duas
    /// sequências do mesmo tamanho, o <c>::</c> vai na <b>primeira</b>.
    /// </para>
    /// </remarks>
    public override string ToString()
    {
        if (EhV4)
        {
            return $"{Bytes[0]}.{Bytes[1]}.{Bytes[2]}.{Bytes[3]}";
        }

        // A regra 5 da RFC 5952, que o teste exaustivo me cobrou: um endereço
        // com IPv4 embutido escreve os 32 bits finais em forma decimal com
        // pontos. São dois casos, e os dois começam com dez bytes zerados:
        //
        //   ::ffff:a.b.c.d   o mapeado, que carrega um IPv4 dentro do IPv6
        //   ::a.b.c.d        o compatível, que a RFC 4291 depreciou em 2006
        //
        // O endereço todo zerado e o ::1 ficam de fora: eles são o endereço
        // indefinido e o laço local, não têm IPv4 dentro, e escrevê-los como
        // ::0.0.0.0 esconderia o que eles são.
        var temV4Embutido = false;

        {
            var dezZerados = true;

            for (var i = 0; i < 10; i++)
            {
                if (Bytes[i] != 0)
                {
                    dezZerados = false;

                    break;
                }
            }

            if (dezZerados)
            {
                var onzeEDoze = (Bytes[10] << 8) | Bytes[11];

                var finalZerado = Bytes[12] == 0 && Bytes[13] == 0
                    && Bytes[14] == 0 && Bytes[15] <= 1;

                temV4Embutido = onzeEDoze == 0xFFFF || (onzeEDoze == 0 && !finalZerado);
            }
        }

        if (temV4Embutido)
        {
            var cauda = $"{Bytes[12]}.{Bytes[13]}.{Bytes[14]}.{Bytes[15]}";

            return ((Bytes[10] << 8) | Bytes[11]) == 0xFFFF ? "::ffff:" + cauda : "::" + cauda;
        }

        var grupos = new ushort[8];

        for (var i = 0; i < 8; i++)
        {
            grupos[i] = (ushort)((Bytes[i * 2] << 8) | Bytes[i * 2 + 1]);
        }

        // Acha a maior sequência de zeros. O desempate é pela primeira, que é o
        // que a RFC 5952 manda e quase todo código caseiro esquece.
        var melhorInicio = -1;
        var melhorTamanho = 0;

        var inicio = -1;

        for (var i = 0; i <= 8; i++)
        {
            if (i < 8 && grupos[i] == 0)
            {
                if (inicio < 0)
                {
                    inicio = i;
                }
            }
            else if (inicio >= 0)
            {
                var tamanho = i - inicio;

                if (tamanho > melhorTamanho)
                {
                    melhorTamanho = tamanho;
                    melhorInicio = inicio;
                }

                inicio = -1;
            }
        }

        // Um grupo só não vira "::": não encurta nada, e a norma proíbe.
        if (melhorTamanho < 2)
        {
            melhorInicio = -1;
        }

        var saida = new StringBuilder();

        for (var i = 0; i < 8; i++)
        {
            if (i == melhorInicio)
            {
                saida.Append("::");

                i += melhorTamanho - 1;

                continue;
            }

            if (i > 0 && saida.Length > 0 && saida[^1] != ':')
            {
                saida.Append(':');
            }

            saida.Append(grupos[i].ToString("x"));
        }

        return saida.Length == 0 ? "::" : saida.ToString();
    }

    public bool Equals(Endereco outro) => Bytes.SequenceEqual(outro.Bytes);

    public override bool Equals(object? outro) => outro is Endereco endereco && Equals(endereco);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.AddBytes(Bytes);

        return hash.ToHashCode();
    }

    /// <summary>Compara pelos bytes. Um IPv4 vem antes de qualquer IPv6.</summary>
    public int CompareTo(Endereco outro)
    {
        if (Bytes.Length != outro.Bytes.Length)
        {
            return Bytes.Length.CompareTo(outro.Bytes.Length);
        }

        return Bytes.SequenceCompareTo(outro.Bytes);
    }

    public static bool operator ==(Endereco um, Endereco outro) => um.Equals(outro);

    public static bool operator !=(Endereco um, Endereco outro) => !um.Equals(outro);

    public static bool operator <(Endereco um, Endereco outro) => um.CompareTo(outro) < 0;

    public static bool operator >(Endereco um, Endereco outro) => um.CompareTo(outro) > 0;

    public static bool operator <=(Endereco um, Endereco outro) => um.CompareTo(outro) <= 0;

    public static bool operator >=(Endereco um, Endereco outro) => um.CompareTo(outro) >= 0;
}
