\set ON_ERROR_STOP on
-- ============================================================
-- 066_catalogo_produto_centro_Q_ROLLBACK_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 066 (Q) - ROLLBACK
--
-- Apaga o ESPELHO local do catalogo. Nenhuma decisao do usuario se perde: os
-- vinculos vivem em produto_etiqueta_vinculo, que este script NAO toca.
--
-- O custo de reverter e tempo: a proxima consulta volta a buscar tudo no SAP, como
-- antes do incremento. No QAS130 isso sao 51 chamadas por abertura de tela.
--
-- ATENCAO: reverter aqui sem reverter o executavel deixa a tela de Vinculo de
-- Produto sem a tabela que ela le, e ela quebra ao abrir. Reverter os dois, ou
-- nenhum.
-- ============================================================

BEGIN;

DO $guarda$
BEGIN
    IF current_database() NOT IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '066 Q ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;
END
$guarda$;

\echo '-- quantos materiais espelhados serao perdidos (recuperaveis do SAP):'
SELECT count(*) AS materiais FROM homologacao.produto_catalogo_centro;

\echo '-- quantos vinculos permanecem intactos:'
SELECT count(*) AS vinculos FROM homologacao.produto_etiqueta_vinculo;

DROP TABLE IF EXISTS homologacao.produto_catalogo_centro;

COMMIT;

\echo '== 066 Q revertido. Os vinculos nao foram tocados. =='
