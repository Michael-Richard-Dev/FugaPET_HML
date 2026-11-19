\set ON_ERROR_STOP on
-- ============================================================
-- 068_processo_pesagem_na_caixa_Q_PROPOSTA_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 068 (Q) - O indice de terminal ativo passa a saber o PROCESSO
-- PROPOSTA (ESCRITA) - transacao unica
--
-- Porte do incremento 068 da linha DEV, aplicado e validado la em 19/11/2026. Mesmo
-- objeto, mesmas regras; muda o schema (homologacao) e o papel, escolhido pelo banco.
--
-- POR QUE EXISTE: o envio unico (processo ENVIO_UNICO do vinculo 065) acumula caixas
-- FINALIZADAS E SEM INTEGRAR ate o operador enviar todas no fim. O indice de hoje
--
--     uq_hu_caixa_terminal_ativo ON hu_caixa (upper(btrim(terminal)))
--     WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
--
-- admite UMA caixa nao-confirmada por terminal. Sob ele o envio unico e
-- estruturalmente impossivel: a SEGUNDA caixa ja e recusada pelo banco.
--
-- A ESCOLHA: o indice passa a aplicar DUAS regras, pelo processo gravado na caixa.
--   CAIXA_A_CAIXA (e caixa sem processo registrado): regra IDENTICA a de hoje. A
--     protecao do caixa a caixa nao e afrouxada em nada.
--   ENVIO_UNICO: ocupa o terminal so o que EXIGE DECISAO HUMANA — caixa aberta na
--     balanca e envio em curso.
--
-- NULL = processo nao registrado, que e o caso de toda caixa anterior a este
-- incremento. 'IS DISTINCT FROM' trata NULL como caixa a caixa, entao as linhas
-- antigas continuam sob a regra sob a qual nasceram. Nenhuma linha e reescrita aqui.
--
-- RISCO DE VIOLACAO AO CRIAR O INDICE: nenhum. O conjunto indexado so DIMINUI, e so
-- para linhas ENVIO_UNICO, que ainda nao existem nestes bancos.
--
-- PRIVILEGIO POR COLUNA: estas bases concedem INSERT por COLUNA em hu_caixa (SELECT de
-- tabela + 22 colunas com INSERT, conferido em 19/11/2026 nos dois). Coluna nova SEM
-- grant explicito faz a pesagem falhar no INSERT, em runtime, na mao do operador — e
-- nenhuma verificacao de catalogo pega isso. O GRANT abaixo nao e opcional.
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
        RAISE EXCEPTION '068 Q ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa') IS NULL THEN
        RAISE EXCEPTION '068 Q ABORTADO: tabela hu_caixa nao existe.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = papel) THEN
        RAISE EXCEPTION '068 Q ABORTADO: papel % nao existe neste banco.', papel;
    END IF;
END
$guarda$;

ALTER TABLE homologacao.hu_caixa
    ADD COLUMN IF NOT EXISTS processo_pesagem varchar(20) NULL;

COMMENT ON COLUMN homologacao.hu_caixa.processo_pesagem IS
    'Processo sob o qual a caixa foi aberta (do vinculo 065). NULO = nao registrado, tratado como CAIXA_A_CAIXA. Governa o indice de terminal ativo.';

DO $check$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conrelid='homologacao.hu_caixa'::regclass
           AND conname='ck_hu_caixa_processo_pesagem')
    THEN
        ALTER TABLE homologacao.hu_caixa
            ADD CONSTRAINT ck_hu_caixa_processo_pesagem
            CHECK (processo_pesagem IS NULL
                   OR processo_pesagem IN ('CAIXA_A_CAIXA','ENVIO_UNICO'));
    END IF;
END
$check$;

-- O indice novo. DROP + CREATE porque predicado de indice parcial nao se altera.
DROP INDEX IF EXISTS homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
    ON homologacao.hu_caixa (upper(btrim(terminal)))
 WHERE (
         -- Caixa a caixa e caixa sem processo registrado: a regra de sempre.
         processo_pesagem IS DISTINCT FROM 'ENVIO_UNICO'
         AND status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
       )
    OR (
         -- Envio unico: so ocupa o terminal o que exige decisao humana.
         processo_pesagem = 'ENVIO_UNICO'
         AND status_hu_caixa IN ('EM_PESAGEM','ENVIANDO_SAP')
       );

-- Sem estes GRANTs a pesagem quebra no INSERT. Dinamicos porque o papel muda por banco.
DO $grants$
DECLARE
    papel text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
BEGIN
    EXECUTE format('GRANT INSERT (processo_pesagem) ON homologacao.hu_caixa TO %I', papel);
    EXECUTE format('GRANT SELECT (processo_pesagem) ON homologacao.hu_caixa TO %I', papel);
    RAISE NOTICE 'Privilegios de coluna concedidos a %.', papel;
END
$grants$;

COMMIT;

\echo '== 068 Q aplicado. Rode a VALIDACAO. =='
