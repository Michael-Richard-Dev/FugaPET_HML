\set ON_ERROR_STOP on
-- ============================================================
-- 067_cadastro_etiqueta_caixa_Q_PROPOSTA_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 067 (Q) - CADASTRO da etiqueta de caixa do produto acabado
-- PROPOSTA (ESCRITA, so DADOS) - transacao unica, IDEMPOTENTE
--
-- ESTE INCREMENTO NAO ALTERA ESTRUTURA. Insere duas linhas de cadastro:
-- um modelo de etiqueta e a etiqueta de caixa que o aponta.
--
-- POR QUE EXISTE: o cadastro de etiqueta esta VAZIO nos dois bancos de homologacao
-- (zero etiquetas, zero modelos, conferido em 19/11/2026). A tela de Vinculo de
-- Produto oferece etiquetas ATIVAS e do tipo CAIXA, logo nao tinha o que listar, e
-- o usuario via a lista vazia. Nao e defeito da tela: e cadastro que nunca foi
-- preenchido nestes ambientes.
--
-- SEM NUMERO PAR NA LINHA DEV, de proposito: no DEV110 ja existem etiquetas
-- cadastradas (dados de teste). Este 067 e exclusivo do Q.
--
-- LEIA ISTO ANTES DE USAR O conteudo_zpl: a etiqueta 8x5 que a impressora produz
-- hoje NAO vem deste cadastro. O ZPL e montado em codigo, em
-- Servicos/ServicoImpressoraZebra.cs, e nenhum caminho de impressao le
-- modelo_etiqueta.conteudo_zpl — conferido por busca no fonte. A coluna e NOT NULL
-- e tem CHECK de nao-vazio, entao algo precisa estar nela; em vez de inventar um
-- ZPL que ninguem imprime (e que imprimiria lixo se alguem tentasse), o conteudo
-- DIZ onde esta a verdade. Quando o vinculo passar a mandar na impressao, este
-- campo recebe o template de verdade.
--
-- As dimensoes sao as do ZPL real, lidas do proprio codigo: 8x5 cm = 3,15 x 1,97
-- polegadas = 640 x 400 dots a 203 dpi.
--
-- IDEMPOTENTE: cada insercao e guardada por NOT EXISTS sobre a chave unica real
-- (upper(trim(...))), que e como o banco julga duplicidade. Rodar duas vezes nao
-- cria segunda linha nem falha.
-- ============================================================

BEGIN;

DO $guarda$
BEGIN
    IF current_database() NOT IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '067 Q ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;
END
$guarda$;

-- 1. O modelo. A etiqueta nao pode existir sem ele: etiqueta.codigo_modelo_etiqueta
--    e NOT NULL com FK para ca.
INSERT INTO homologacao.modelo_etiqueta
    (nome_modelo_etiqueta, versao, largura_mm, altura_mm, dpi, conteudo_zpl, observacao, situacao_modelo_etiqueta)
SELECT
    'Caixa Produto Acabado 8x5',
    1,
    80.00,
    50.00,
    203,
    '^XA^FX O ZPL real desta etiqueta e montado em codigo, em Servicos/ServicoImpressoraZebra.cs. Este cadastro registra a etiqueta; nao a imprime. Nao use este conteudo para imprimir.^FS^XZ',
    'Cadastrado em 19/11/2026 para que a tela de Vinculo de Produto tenha a etiqueta de caixa a oferecer. Dimensoes conferidas no ZPL real: 640x400 dots a 203 dpi.',
    true
WHERE NOT EXISTS (
    SELECT 1 FROM homologacao.modelo_etiqueta
     WHERE upper(btrim(nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5'))
       AND versao = 1);

-- 2. A etiqueta de caixa, apontando para o modelo acima (localizado pela chave
--    unica, nao por codigo fixo: a sequencia nao e a mesma nos dois bancos).
INSERT INTO homologacao.etiqueta
    (codigo_modelo_etiqueta, codigo_interno, nome_etiqueta, tipo_etiqueta, descricao_etiqueta, situacao_etiqueta)
SELECT
    m.codigo_modelo_etiqueta,
    'CAIXA-PA-8X5',
    'Caixa Produto Acabado 8x5',
    'CAIXA',
    'Etiqueta de caixa da saida de produto acabado. E a unica valida para o vinculo produto x etiqueta.',
    true
  FROM homologacao.modelo_etiqueta m
 WHERE upper(btrim(m.nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5'))
   AND m.versao = 1
   AND NOT EXISTS (
       SELECT 1 FROM homologacao.etiqueta
        WHERE upper(btrim(codigo_interno)) = upper(btrim('CAIXA-PA-8X5')));

DO $conta$
DECLARE
    modelos integer;
    etiquetas integer;
BEGIN
    SELECT count(*) INTO modelos FROM homologacao.modelo_etiqueta
     WHERE upper(btrim(nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5')) AND versao = 1;
    SELECT count(*) INTO etiquetas FROM homologacao.etiqueta
     WHERE upper(btrim(codigo_interno)) = upper(btrim('CAIXA-PA-8X5'));

    IF modelos <> 1 OR etiquetas <> 1 THEN
        RAISE EXCEPTION '067 Q ABORTADO: esperado 1 modelo e 1 etiqueta, achei % e %.', modelos, etiquetas;
    END IF;

    RAISE NOTICE 'OK: 1 modelo e 1 etiqueta CAIXA ativa em %.', current_database();
END
$conta$;

COMMIT;

\echo '== 067 Q aplicado. Rode a VALIDACAO. =='
