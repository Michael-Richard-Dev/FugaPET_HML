\set ON_ERROR_STOP on
-- ============================================================
-- 066_catalogo_produto_centro_Q_PROPOSTA_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 066 (Q) - Catalogo LOCAL de produtos do centro
-- PROPOSTA (ESCRITA) - transacao unica
--
-- Porte do incremento 066 da linha DEV, onde foi aplicado e validado em
-- 18/11/2026. Mesmo objeto, mesmas regras; muda o schema (homologacao) e o papel
-- de aplicacao, escolhido pelo banco corrente.
--
-- POR QUE EXISTE: ate aqui a tela de Vinculo de Produto consultava o SAP a CADA
-- abertura. Medido contra o SAP real do DEV110 (centro 3007, 155 materiais), abrir
-- a tela custava 7 chamadas; com o catalogo local custa ZERO, e a sincronizacao
-- sem novidade custa 1 chamada. No QAS130, cujo centro tem 1521 materiais, a conta
-- antiga era 51 chamadas POR ABERTURA: 8 paginas de listagem, 39 lotes de tipo e
-- 4 de descricao. Pedido do usuario em 18/11/2026: guardar o catalogo aqui e
-- buscar no SAP apenas o que for NOVO.
--
-- POR QUE ISTO E OBRIGATORIO ANTES DE PUBLICAR O EXECUTAVEL: a tela de Vinculo de
-- Produto passou a LER desta tabela em vez do SAP. Sem ela, a tela quebra ao abrir
-- com "relation does not exist" — nao degrada, quebra.
--
-- O QUE ESTA TABELA E, E O QUE NAO E:
--   E um ESPELHO do que o SAP respondeu na ultima sincronizacao, com a data em que
--   respondeu. Serve para a tela abrir sem rede e para a sincronizacao saber o que
--   ja conhece.
--   NAO e fonte de verdade do cadastro do produto: a verdade continua no SAP.
--   Nenhuma decisao de negocio deve ler daqui como se fosse o mestre atual.
--
-- O CATALOGO GUARDA TODOS OS TIPOS DE MATERIAL, nao so os acabados. E o que permite
-- gravar "ja perguntei por este e ele e ROH" e nunca mais perguntar. Filtrar antes
-- de gravar faria a sincronizacao repetir a pergunta a cada rodada; o filtro de
-- exibicao vive na aplicacao.
--
-- RELACAO COM produto_etiqueta_vinculo (065): sao tabelas INDEPENDENTES, de
-- proposito, e NAO ha chave estrangeira entre elas. O vinculo e decisao do usuario
-- e sobrevive ao produto sumir do catalogo; amarrar as duas faria a sincronizacao
-- poder apagar decisao de gente.
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
        RAISE EXCEPTION '066 Q ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = papel) THEN
        RAISE EXCEPTION '066 Q ABORTADO: papel % nao existe neste banco.', papel;
    END IF;
END
$guarda$;

CREATE TABLE IF NOT EXISTS homologacao.produto_catalogo_centro
(
    codigo_catalogo     bigserial    NOT NULL,
    centro              text         NOT NULL,
    produto             text         NOT NULL,

    descricao           text         NULL,
    tipo_material       text         NULL,
    unidade_base        text         NULL,
    marcado_eliminacao  boolean      NOT NULL DEFAULT false,

    -- Quando o SAP confirmou que este material existe no centro. Atualizada a cada
    -- sincronizacao em que ele aparece.
    visto_em            timestamptz  NOT NULL DEFAULT now(),

    -- Quando os dados mestres (tipo, unidade, descricao) foram lidos do SAP. Fica
    -- nulo enquanto so se sabe que o material existe no centro - e esse nulo que
    -- diz a sincronizacao o que ainda falta buscar.
    mestre_lido_em      timestamptz  NULL,

    -- false = o SAP parou de listar este material neste centro. NAO se apaga a
    -- linha: pode haver vinculo decidido sobre ela, e sumir em silencio seria pior
    -- do que mostrar que sumiu.
    presente_no_sap     boolean      NOT NULL DEFAULT true,

    criado_em           timestamptz  NOT NULL DEFAULT now(),
    atualizado_em       timestamptz  NULL,

    CONSTRAINT pk_produto_catalogo_centro
        PRIMARY KEY (codigo_catalogo),

    CONSTRAINT uq_produto_catalogo_centro_produto
        UNIQUE (centro, produto),

    CONSTRAINT ck_produto_catalogo_centro_centro
        CHECK (btrim(centro) <> ''),

    CONSTRAINT ck_produto_catalogo_centro_produto
        CHECK (btrim(produto) <> '')
);

COMMENT ON TABLE homologacao.produto_catalogo_centro IS
    'Espelho local dos materiais de um centro, como o SAP respondeu na ultima sincronizacao. NAO e fonte de verdade do cadastro do produto.';

COMMENT ON COLUMN homologacao.produto_catalogo_centro.mestre_lido_em IS
    'Nulo = ainda falta buscar tipo/unidade/descricao no SAP. E o que torna a sincronizacao incremental.';

COMMENT ON COLUMN homologacao.produto_catalogo_centro.presente_no_sap IS
    'false = o SAP parou de listar o material neste centro. A linha permanece porque pode haver vinculo decidido sobre ela.';

-- Leitura da tela: materiais de um centro, ordenados por codigo.
CREATE INDEX IF NOT EXISTS ix_produto_catalogo_centro_centro
    ON homologacao.produto_catalogo_centro (centro, produto);

-- "O que ainda falta buscar?" - a pergunta que a sincronizacao faz toda vez.
CREATE INDEX IF NOT EXISTS ix_produto_catalogo_centro_pendente
    ON homologacao.produto_catalogo_centro (centro)
    WHERE mestre_lido_em IS NULL;

-- Os GRANTs sao dinamicos porque o papel muda entre os dois bancos.
DO $grants$
DECLARE
    papel text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
BEGIN
    EXECUTE format(
        'GRANT SELECT, INSERT, UPDATE, DELETE ON homologacao.produto_catalogo_centro TO %I', papel);
    EXECUTE format(
        'GRANT USAGE, SELECT ON SEQUENCE homologacao.produto_catalogo_centro_codigo_catalogo_seq TO %I', papel);
    RAISE NOTICE 'Privilegios concedidos a %.', papel;
END
$grants$;

COMMIT;

\echo '== 066 Q aplicado. Rode a VALIDACAO. =='
