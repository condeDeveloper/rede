# rede

Endereços IP e faixas CIDR do zero em C# e .NET 8, com a forma canônica de IPv6
da RFC 5952.

```
$ dotnet medidor.dll faixa 192.168.1.0/24

  rede          192.168.1.0
  mascara       255.255.255.0
  ultimo        192.168.1.255
  broadcast     192.168.1.255
  enderecos     256
  usaveis       254

  partida em    192.168.1.0/25  e  192.168.1.128/25
  dentro de     192.168.0.0/23
```

## O juiz é o `System.Net`

O `IPAddress` analisa e escreve endereços, e o `IPNetwork`, que chegou no .NET 8,
responde se um endereço está dentro de uma faixa. Os dois são o que o resto da
plataforma usa, então comparar com eles é comparar com o que um servidor ASP.NET
de verdade vai fazer. E é um juiz ilimitado: responde para qualquer entrada que
eu quiser gerar.

São os **33 prefixos de IPv4** vezes 200 faixas vezes 20 endereços, os **129
prefixos de IPv6** do mesmo jeito, os **16.777.216 endereços** do `10.0.0.0/8` um
por um, e 200 mil endereços IPv6 sorteados para a forma canônica.

Mais as invariantes, que não dependem de implementação nenhuma: partir uma faixa
e somar as metades dá o tamanho original, e duas faixas CIDR ou são disjuntas ou
uma contém a outra. São 44 testes.

## O defeito que o teste exaustivo achou

Os 200 mil endereços IPv6 reprovaram num caso:

```
os bytes 0000000000000000000000005A1DA92E
dão "::90.29.169.46" no .NET e "::5a1d:a92e" aqui
```

É a regra 5 da RFC 5952, que eu não tinha implementado: um endereço com IPv4
embutido escreve os 32 bits finais em forma decimal com pontos. São dois casos, e
os dois começam com dez bytes zerados: `::ffff:a.b.c.d`, o mapeado, e `::a.b.c.d`,
o compatível, que a RFC 4291 depreciou em 2006.

O endereço todo zerado e o `::1` ficam de fora, porque são o endereço indefinido e
o laço local, não têm IPv4 dentro, e escrevê-los como `::0.0.0.0` esconderia o que
eles são.

## As grafias de IPv6

```
$ dotnet medidor.dll canonica

  como veio                                 canonica          igual ao .NET?
  ----------------------------------------------------------------------------
  2001:0db8:0000:0000:0000:0000:0000:0001   2001:db8::1        sim
  2001:DB8::1                               2001:db8::1        sim
  2001:db8:0:0:0:0:0:1                      2001:db8::1        sim
  2001:0:0:1:0:0:0:1                        2001:0:0:1::1      sim
  2001:db8:0:0:1:0:0:1                      2001:db8::1:0:0:1  sim
  2001:db8:0:1:1:1:1:1                      2001:db8:0:1:1:1:1:1  sim
```

Sem a RFC 5952, as três primeiras linhas são três endereços diferentes para quem
compara texto. Numa lista de bloqueio ou num registro de acesso, isso é um buraco.

As duas regras que quase todo código caseiro erra estão nas linhas de baixo: o
`::` cobre a **maior** sequência de zeros, e no empate vai na **primeira**. E um
grupo zerado sozinho não vira `::`, porque não encurta nada.

## O endereço em octal que fura filtro

```
$ dotnet medidor.dll octal

    como escrito                  o inet_aton do C                aqui
  ----------------------------------------------------------------------
    169.254.169.254               169.254.169.254                 169.254.169.254
    0251.0376.0251.0376           169.254.169.254 (octal)         recusado
    0xA9.0xFE.0xA9.0xFE           169.254.169.254 (hexa)           recusado
    2852039166                    169.254.169.254 (um numero so)   recusado
    169.16689662                  169.254.169.254 (duas partes)    recusado
```

`169.254.169.254` é o endereço de metadados da AWS, do Google e do Azure, e quem
chega nele de dentro de uma máquina lê credenciais. Um filtro que o recuse pelo
texto costuma deixar passar a forma octal, porque o `inet_aton` do C aceita as
cinco formas acima e o sistema operacional resolve todas para o mesmo lugar.

Este leitor aceita só a forma de quatro números decimais, sem zero à esquerda.
Ser mais estrito que o sistema é a escolha certa num filtro: o custo é recusar
uma grafia que ninguém escreve de propósito.

## O `/31` e o `/32`, que quase todo código erra

A conta "quantos endereços menos dois" vale até o `/30`. Depois disso não:

| faixa | endereços | usáveis | por quê |
|---|---|---|---|
| `/24` | 256 | 254 | o primeiro é a rede, o último é broadcast |
| `/30` | 4 | 2 | idem |
| `/31` | 2 | **2** | RFC 3021: numa ligação ponto a ponto não há broadcast |
| `/32` | 1 | **1** | é um endereço só, que é ele mesmo |

Reservar dois de dois endereços num `/31` deixaria zero, e é por isso que a RFC
3021 existe. No IPv6 não há broadcast nenhum, então todos são sempre usáveis.

## Duas faixas nunca se sobrepõem pela metade

Ou são disjuntas, ou uma contém a outra. Não é coincidência: é consequência de o
tamanho ser sempre potência de dois e o começo ser alinhado. Um teste confere isso
em 200 mil pares, comparando com a conta feita pelos endereços de início e fim.

É essa propriedade que permite guardar rotas numa árvore em vez de numa lista, e
é a razão de o CIDR ter salvado a internet em 1993. Antes dele os endereços vinham
em três tamanhos fixos, e uma empresa com 300 máquinas recebia uma classe B
inteira, jogando fora 65 mil endereços.

## As faixas reservadas

```
$ dotnet medidor.dll privadas

  Somando: 307.364.608 enderecos de 4.294.967.296, ou 7.2%
```

As três faixas de documentação da RFC 5737 (`192.0.2.0/24`, `198.51.100.0/24` e
`203.0.113.0/24`) existem para que exemplo em livro e em manual não aponte para a
máquina de ninguém. São as únicas que podem aparecer num texto publicado sem
incomodar alguém.

## Rodar

.NET 8. Zero dependências fora do xUnit, e só nos testes.

```
dotnet test testes/Rede.Testes/Rede.Testes.csproj -c Release
dotnet run --project ferramentas/Medidor/Medidor.csproj -c Release -- canonica
```

As medidas de tempo moram na ferramenta e não na suíte, de propósito: uma
afirmação de relógio falha sem que nada tenha piorado, basta a máquina do CI estar
ocupada.

## O que ele não faz

Não resolve nome, não fala com a rede e não abre soquete. É aritmética de
endereço, e só.

Não tem zona de IPv6 (o `%eth0` no fim de um link-local), que importa quando a
mesma faixa `fe80::/10` existe em várias interfaces da mesma máquina.

Não tem tabela de rotas com casamento do prefixo mais longo. A estrutura certa é
uma árvore binária de prefixos, e ela merece um projeto só.

Não tem a lista de faixas reservadas da IANA embutida como dado consultável: ela
muda, e uma cópia desatualizada dentro de uma biblioteca é pior que nenhuma.

## Licença

MIT.
