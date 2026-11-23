\set ON_ERROR_STOP on
-- ============================================================
-- 069_tolerancia_recebimento_material_Q_PROPOSTA_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 069 (Q) - Tolerancia de pesagem por material no Recebimento
-- PROPOSTA (ESCRITA) - transacao unica
--
-- Porte do incremento 069 da linha DEV, aplicado e validado la em 23/11/2026. Mesmo
-- objeto e mesmas regras; muda o schema (homologacao) e o papel, escolhido pelo banco.
--
-- O QUE CRIA: a decisao do usuario sobre QUANTO a pesagem pode divergir da quantidade
-- do pedido, por material do centro. O catalogo de materiais NAO mora aqui — vem do
-- SAP, espelhado em produto_catalogo_centro (incremento 066).
--
-- A REGRA, em uma frase: recebido o item, o total LIQUIDO pesado precisa ficar entre
-- (quantidade do pedido - X%) e (quantidade do pedido + X%).
--
-- DUAS REGRAS ESTAO NO BANCO, nao so na tela, porque sao de integridade:
--   1. UNIQUE (centro, material): um material tem no maximo UMA tolerancia. Duas
--      linhas fariam a Entrada escolher entre elas, e nao ha criterio para isso.
--   2. CHECK 0 <= percentual <= 100: acima de 100 o limite inferior ficaria NEGATIVO,
--      o que nao significa nada. Zero e valido e significa exigencia exata.
--
-- POR QUE percentual E NOT NULL: a ausencia de tolerancia se expressa pela AUSENCIA DE
-- LINHA, nao por um nulo. Nulo criaria um terceiro estado ("cadastrado mas sem valor")
-- que a tela teria de explicar e a Entrada, de tratar.
--
-- EFEITO OPERACIONAL QUE PRECISA SER SABIDO: por decisao do usuario, material do
-- recebimento SEM tolerancia cadastrada fica BLOQUEADO na pesagem. Aplicar este
-- incremento nao bloqueia nada sozinho (a tabela nasce vazia e o app so passa a
-- cobrar quando a versao nova estiver publicada), mas assim que o executavel novo
-- rodar, os materiais sem cadastro param. Cadastre antes de publicar, ou publique
-- sabendo disso.
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
        RAISE EXCEPTION '069 Q ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = papel) THEN
        RAISE EXCEPTION '069 Q ABORTADO: papel % nao existe neste banco.', papel;
    END IF;
END
$guarda$;

CREATE TABLE IF NOT EXISTS homologacao.tolerancia_recebimento_material
(
    codigo_tolerancia      bigserial     NOT NULL,
    centro                 text          NOT NULL,
    material               text          NOT NULL,

    -- Percentual unico, valido para MAIS e para MENOS (decisao do usuario em
    -- 23/11/2026). Dois percentuais seriam dois campos para manter e conferir, e
    -- nenhum caso real pedia assimetria.
    percentual_tolerancia  numeric(5,2)  NOT NULL,

    -- Retrato do SAP no momento do cadastro: faz a tela abrir sem rede e deixa auditar
    -- o que o usuario via quando decidiu. NAO e fonte de verdade.
    descricao_material     text          NULL,
    tipo_material          text          NULL,

    situacao               boolean       NOT NULL DEFAULT true,
    criado_em              timestamptz   NOT NULL DEFAULT now(),
    criado_por             bigint        NULL,
    atualizado_em          timestamptz   NULL,
    atualizado_por         bigint        NULL,

    CONSTRAINT pk_tolerancia_recebimento_material
        PRIMARY KEY (codigo_tolerancia),

    CONSTRAINT uq_tolerancia_recebimento_material
        UNIQUE (centro, material),

    CONSTRAINT ck_tolerancia_recebimento_percentual
        CHECK (percentual_tolerancia >= 0 AND percentual_tolerancia <= 100),

    CONSTRAINT ck_tolerancia_recebimento_centro
        CHECK (btrim(centro) <> ''),

    CONSTRAINT ck_tolerancia_recebimento_material_vazio
        CHECK (btrim(material) <> '')
);

COMMENT ON TABLE homologacao.tolerancia_recebimento_material IS
    'Quanto a pesagem do Recebimento pode divergir da quantidade do pedido, por material do centro. O catalogo de materiais NAO mora aqui - vem do SAP (produto_catalogo_centro, incremento 066).';

COMMENT ON COLUMN homologacao.tolerancia_recebimento_material.percentual_tolerancia IS
    'Percentual unico, valido para mais E para menos. 10.00 = o total liquido pesado pode ficar entre 90% e 110% da quantidade pedida. Zero = exigencia exata.';

COMMENT ON COLUMN homologacao.tolerancia_recebimento_material.situacao IS
    'false = tolerancia inativa. A Entrada trata inativa como NAO CADASTRADA, e nao como "sem limite".';

-- Busca da tela: lista por centro, ordenada por material.
CREATE INDEX IF NOT EXISTS ix_tolerancia_recebimento_centro
    ON homologacao.tolerancia_recebimento_material (centro, material);

-- GRANT para o papel do banco em que estamos. Dinamico porque o papel difere entre
-- QAS110 e QAS130 e o script e UM so para os dois.
DO $permissoes$
DECLARE
    papel text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
BEGIN
    EXECUTE format(
        'GRANT SELECT, INSERT, UPDATE, DELETE ON homologacao.tolerancia_recebimento_material TO %I', papel);
    EXECUTE format(
        'GRANT USAGE, SELECT ON SEQUENCE homologacao.tolerancia_recebimento_material_codigo_tolerancia_seq TO %I', papel);
    RAISE NOTICE 'permissoes concedidas a %', papel;
END
$permissoes$;

COMMIT;

\echo '== 069 Q aplicado. Rode a VALIDACAO. =='
