\set ON_ERROR_STOP on
-- ============================================================
-- 056_REV2_PROPOSTA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Exclusividade duravel do item do pedido (fonte do GATE P5)
-- PROPOSTA (ESCRITA) - transacao unica
--
-- Substitui o 056_Q original, que nunca foi aplicado e tinha GRANT cravado em
-- fugapet_q_app e nenhuma guarda de alvo. Ver o PREFLIGHT para o porque.
--
-- O QUE E ESTA TABELA: a prova DURAVEL de que um item de pedido ja teve o seu
-- recebimento reivindicado por um lancamento. E uma das tres fontes do GATE P5,
-- que exige prova de ausencia de recebimento ANTES do movimento 101 — e no qual
-- tudo que nao for "comprovadamente sem recebimento" BLOQUEIA.
--
-- A CHAVE E (numero_pedido, numero_item), e e ela que faz a exclusividade: o
-- segundo lancamento que tentar o mesmo item colide na primary key em vez de
-- conseguir um segundo recebimento. A garantia e do BANCO, nao de consulta
-- previa no aplicativo — verificar antes e gravar depois deixa uma janela entre
-- as duas coisas.
--
-- Forma conferida contra o banco do DEV110 em 24/11/2026.
--
-- Leia o PREFLIGHT antes.
-- ============================================================

BEGIN;

DO $guarda$
DECLARE
    papel text;
BEGIN
    papel := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;

    IF papel IS NULL THEN
        RAISE EXCEPTION '056 REV2 ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = papel) THEN
        RAISE EXCEPTION '056 REV2 ABORTADO: papel % nao existe neste banco.', papel;
    END IF;
END
$guarda$;

CREATE TABLE IF NOT EXISTS homologacao.entrada_mp_p5_exclusividade_item_pedido
(
    numero_pedido      text         NOT NULL,
    numero_item        text         NOT NULL,
    codigo_lancamento  bigint       NOT NULL,
    obtida_em          timestamptz  NOT NULL DEFAULT clock_timestamp(),

    CONSTRAINT pk_entrada_mp_p5_exclusividade_item_pedido
        PRIMARY KEY (numero_pedido, numero_item),

    CONSTRAINT ck_entrada_mp_p5_exclusividade_pedido_formato
        CHECK (numero_pedido ~ '^[0-9]{1,10}$'),

    CONSTRAINT ck_entrada_mp_p5_exclusividade_item_formato
        CHECK (numero_item ~ '^[0-9]{5}$'),

    CONSTRAINT ck_entrada_mp_p5_exclusividade_lancamento_positivo
        CHECK (codigo_lancamento > 0)
);

COMMENT ON TABLE homologacao.entrada_mp_p5_exclusividade_item_pedido IS
    'Exclusividade DURAVEL do item do pedido: fonte do GATE P5 antes do movimento 101. A primary key (numero_pedido, numero_item) e a propria garantia - o segundo lancamento colide em vez de obter um segundo recebimento.';

COMMENT ON COLUMN homologacao.entrada_mp_p5_exclusividade_item_pedido.obtida_em IS
    'clock_timestamp(), nao now(): marca o instante REAL da gravacao, e nao o inicio da transacao.';

-- "Quais itens este lancamento reivindicou?" — a pergunta da liberacao/rollback.
CREATE INDEX IF NOT EXISTS ix_entrada_mp_p5_exclusividade_lancamento
    ON homologacao.entrada_mp_p5_exclusividade_item_pedido (codigo_lancamento);

-- GRANT dinamico pelo banco corrente. SELECT e INSERT apenas: a exclusividade e
-- obtida e lida, nunca alterada. Sem UPDATE/DELETE de proposito — reescrever uma
-- reivindicacao ja gravada apagaria a prova que o gate consulta.
DO $permissoes$
DECLARE
    papel text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
BEGIN
    EXECUTE format(
        'GRANT SELECT, INSERT ON homologacao.entrada_mp_p5_exclusividade_item_pedido TO %I', papel);
    RAISE NOTICE 'permissoes concedidas a %', papel;
END
$permissoes$;

COMMIT;

\echo '== 056 REV2 aplicado. Rode a VALIDACAO. =='
