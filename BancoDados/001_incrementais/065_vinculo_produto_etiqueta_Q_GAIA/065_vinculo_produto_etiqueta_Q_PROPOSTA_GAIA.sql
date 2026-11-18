\set ON_ERROR_STOP on
-- ============================================================
-- 065_vinculo_produto_etiqueta_Q_PROPOSTA_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 065 (Q) - PROPOSTA (ESCRITA) - transacao unica
--
-- Porte do incremento 065 da linha DEV, onde ja foi aplicado e validado em
-- 18/11/2026. Mesmo objeto, mesmas regras; muda o schema (homologacao) e o papel
-- de aplicacao, escolhido pelo banco corrente.
--
-- POR QUE ISTO E OBRIGATORIO ANTES DE PUBLICAR O EXECUTAVEL: alem da tela de
-- Vinculo de Produto, a INATIVACAO DE ETIQUETA passou a consultar esta tabela. Sem
-- ela, uma funcionalidade que ja existia quebra com "relation does not exist".
--
-- TRES REGRAS ficam no banco, nao so na tela, porque sao de integridade:
--   1. UNIQUE (centro, produto): um produto tem no maximo UM vinculo, logo nunca
--      duas etiquetas. Pedido explicito do usuario ("apenas um pra um").
--   2. CHECK sem_etiqueta x codigo_etiqueta: "sem etiqueta" e uma ESCOLHA gravada,
--      diferente de "ainda nao configurado", que e a AUSENCIA de linha.
--   3. CHECK processo_pesagem: so os dois processos que existem no dominio.
--
-- ATENCAO ao processo ENVIO_UNICO: a pesagem que junta todas as caixas num unico
-- envio ao SAP AINDA NAO EXISTE (18/11/2026). A coluna aceita o valor porque o
-- usuario precisa cadastrar a intencao antes; nada no runtime deve supor que um
-- produto marcado assim ja tenha caminho de envio.
--
-- Leia o PREFLIGHT antes.
-- ============================================================

BEGIN;

DO $$
DECLARE
    papel text;
BEGIN
    papel := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;

    IF papel IS NULL THEN
        RAISE EXCEPTION '065 Q ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF to_regclass('homologacao.etiqueta') IS NULL THEN
        RAISE EXCEPTION '065 Q ABORTADO: tabela etiqueta nao existe; a FK nasceria quebrada.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = papel) THEN
        RAISE EXCEPTION '065 Q ABORTADO: papel % nao existe neste banco.', papel;
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS homologacao.produto_etiqueta_vinculo
(
    codigo_vinculo     bigserial    NOT NULL,
    centro             text         NOT NULL,
    produto            text         NOT NULL,

    -- Retrato do SAP no momento do vinculo. Serve para a tela mostrar a linha mesmo
    -- com o SAP fora do ar, e para auditar o que o usuario via quando decidiu.
    -- NAO e fonte de verdade: a verdade do cadastro do produto e sempre o SAP.
    descricao_produto  text         NULL,
    tipo_produto       text         NULL,

    codigo_etiqueta    bigint       NULL,
    sem_etiqueta       boolean      NOT NULL DEFAULT false,
    processo_pesagem   text         NOT NULL,

    situacao           boolean      NOT NULL DEFAULT true,
    criado_em          timestamptz  NOT NULL DEFAULT now(),
    criado_por         bigint       NULL,
    atualizado_em      timestamptz  NULL,
    atualizado_por     bigint       NULL,

    CONSTRAINT pk_produto_etiqueta_vinculo
        PRIMARY KEY (codigo_vinculo),

    CONSTRAINT uq_produto_etiqueta_vinculo_produto
        UNIQUE (centro, produto),

    CONSTRAINT fk_produto_etiqueta_vinculo_etiqueta
        FOREIGN KEY (codigo_etiqueta)
        REFERENCES homologacao.etiqueta (codigo_etiqueta),

    CONSTRAINT ck_produto_etiqueta_vinculo_etiqueta
        CHECK ((sem_etiqueta AND codigo_etiqueta IS NULL)
            OR (NOT sem_etiqueta AND codigo_etiqueta IS NOT NULL)),

    CONSTRAINT ck_produto_etiqueta_vinculo_processo
        CHECK (processo_pesagem IN ('CAIXA_A_CAIXA', 'ENVIO_UNICO')),

    CONSTRAINT ck_produto_etiqueta_vinculo_centro
        CHECK (btrim(centro) <> ''),

    CONSTRAINT ck_produto_etiqueta_vinculo_produto_vazio
        CHECK (btrim(produto) <> '')
);

COMMENT ON TABLE homologacao.produto_etiqueta_vinculo IS
    'Decisao do usuario por produto acabado do centro: qual etiqueta e qual processo de pesagem. O catalogo de produtos NAO mora aqui - vem do SAP (A_ProductPlant/A_Product).';

COMMENT ON COLUMN homologacao.produto_etiqueta_vinculo.sem_etiqueta IS
    'true = o usuario decidiu que este produto NAO leva etiqueta. Ausencia de linha = ainda nao decidido.';

COMMENT ON COLUMN homologacao.produto_etiqueta_vinculo.processo_pesagem IS
    'CAIXA_A_CAIXA (implementado) ou ENVIO_UNICO (cadastravel, SEM caminho de envio em 18/11/2026).';

CREATE INDEX IF NOT EXISTS ix_produto_etiqueta_vinculo_centro
    ON homologacao.produto_etiqueta_vinculo (centro, produto);

-- "Quais produtos usam esta etiqueta?" - pergunta da inativacao de etiqueta.
CREATE INDEX IF NOT EXISTS ix_produto_etiqueta_vinculo_etiqueta
    ON homologacao.produto_etiqueta_vinculo (codigo_etiqueta)
    WHERE codigo_etiqueta IS NOT NULL;

DO $$
DECLARE
    papel text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
BEGIN
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON homologacao.produto_etiqueta_vinculo TO %I', papel);
    EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE homologacao.produto_etiqueta_vinculo_codigo_vinculo_seq TO %I', papel);
    RAISE NOTICE 'Privilegios concedidos a %.', papel;
END
$$;

COMMIT;

\echo '== 065 Q aplicado. Rode a VALIDACAO. =='
