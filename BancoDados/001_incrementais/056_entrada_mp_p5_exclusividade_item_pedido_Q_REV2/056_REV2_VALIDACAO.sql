\set ON_ERROR_STOP on
-- ============================================================
-- 056_REV2_VALIDACAO.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- VALIDACAO (SOMENTE LEITURA) - nao escreve nada
--
-- Prova pela DEFINICAO REAL lida do catalogo, nunca inserindo linhas: escrever
-- exigiria permissao, sujaria a tabela e poderia afirmar sucesso mesmo com a
-- restricao aplicada diferente da pretendida.
--
-- O item mais importante e o 4: o GRANT tem de estar no papel DESTE banco. O
-- script antigo cravava fugapet_q_app, e como esse papel existe tambem no
-- QAS130, o erro passaria em silencio e so apareceria como bloqueio do GATE P5
-- na mao do operador.
-- ============================================================

\echo '== 056 REV2 VALIDACAO =='

SELECT current_database() AS banco, current_user AS usuario;

\echo '-- 1. estrutura'
SELECT CASE WHEN to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido') IS NULL
            THEN 'FALHOU: tabela nao existe' ELSE 'OK: tabela existe' END AS tabela;

WITH esperado(coluna, tipo, nulavel) AS (
    VALUES ('numero_pedido',     'text',   'NO'),
           ('numero_item',       'text',   'NO'),
           ('codigo_lancamento', 'bigint', 'NO'),
           ('obtida_em',         'timestamp with time zone', 'NO')
)
SELECT e.coluna,
       CASE WHEN c.column_name IS NULL THEN 'FALHOU: ausente'
            WHEN c.data_type <> e.tipo THEN 'FALHOU: tipo ' || c.data_type
            WHEN c.is_nullable <> e.nulavel THEN 'FALHOU: nulavel ' || c.is_nullable
            ELSE 'OK'
       END AS veredito
  FROM esperado e
  LEFT JOIN information_schema.columns c
         ON c.table_schema='homologacao'
        AND c.table_name='entrada_mp_p5_exclusividade_item_pedido'
        AND c.column_name=e.coluna
 ORDER BY e.coluna;

\echo '-- 2. a chave que PRODUZ a exclusividade'
SELECT CASE
         WHEN EXISTS (SELECT 1 FROM pg_constraint
                       WHERE conrelid = to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido')
                         AND contype='p'
                         AND pg_get_constraintdef(oid) = 'PRIMARY KEY (numero_pedido, numero_item)')
           THEN 'OK: PRIMARY KEY (numero_pedido, numero_item)'
         ELSE 'FALHOU: chave ausente ou com outra forma - a exclusividade NAO esta garantida'
       END AS veredito;

\echo '-- 3. os CHECK de formato, avaliados contra tabela-verdade'
DO $validacao$
DECLARE
    expr_pedido text;
    expr_item   text;
    caso        record;
    resultado   boolean;
    falhas      int := 0;
BEGIN
    SELECT pg_get_expr(conbin, conrelid) INTO expr_pedido FROM pg_constraint
     WHERE conrelid = to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido')
       AND conname  = 'ck_entrada_mp_p5_exclusividade_pedido_formato';
    SELECT pg_get_expr(conbin, conrelid) INTO expr_item FROM pg_constraint
     WHERE conrelid = to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido')
       AND conname  = 'ck_entrada_mp_p5_exclusividade_item_formato';

    IF expr_pedido IS NULL OR expr_item IS NULL THEN
        RAISE EXCEPTION 'FALHOU: um dos CHECK de formato nao existe.';
    END IF;
    RAISE NOTICE 'pedido: %', expr_pedido;
    RAISE NOTICE 'item  : %', expr_item;

    FOR caso IN SELECT * FROM (VALUES
        ('4500001234', true), ('1', true), ('12345678901', false), ('abc', false), ('', false)
    ) AS t(valor, aceito) LOOP
        EXECUTE format('SELECT (%s)', replace(expr_pedido, 'numero_pedido', quote_literal(caso.valor))) INTO resultado;
        IF resultado IS DISTINCT FROM caso.aceito THEN
            falhas := falhas + 1;
            RAISE WARNING 'FALHOU pedido %: esperado % e veio %', caso.valor, caso.aceito, resultado;
        END IF;
    END LOOP;

    FOR caso IN SELECT * FROM (VALUES
        ('00010', true), ('1', false), ('000100', false), ('abcde', false)
    ) AS t(valor, aceito) LOOP
        EXECUTE format('SELECT (%s)', replace(expr_item, 'numero_item', quote_literal(caso.valor))) INTO resultado;
        IF resultado IS DISTINCT FROM caso.aceito THEN
            falhas := falhas + 1;
            RAISE WARNING 'FALHOU item %: esperado % e veio %', caso.valor, caso.aceito, resultado;
        END IF;
    END LOOP;

    IF falhas > 0 THEN
        RAISE EXCEPTION 'FALHOU: % caso(s) divergentes nos CHECK de formato.', falhas;
    END IF;
    RAISE NOTICE 'OK: CHECK de formato de pedido e item corretos (9 casos).';
END
$validacao$;

\echo '-- 4. permissoes no papel DESTE banco (o erro do script antigo)'
DO $permissoes$
DECLARE
    papel  text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
    outro  text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q130_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q_app'
    END;
    falhas int := 0;
BEGIN
    IF papel IS NULL THEN
        RAISE EXCEPTION 'FALHOU: banco % nao reconhecido.', current_database();
    END IF;

    IF NOT has_table_privilege(papel, 'homologacao.entrada_mp_p5_exclusividade_item_pedido', 'SELECT') THEN
        falhas := falhas + 1; RAISE WARNING 'FALHOU: % sem SELECT', papel;
    END IF;
    IF NOT has_table_privilege(papel, 'homologacao.entrada_mp_p5_exclusividade_item_pedido', 'INSERT') THEN
        falhas := falhas + 1; RAISE WARNING 'FALHOU: % sem INSERT', papel;
    END IF;

    -- Nao e falha se o outro papel tambem tiver acesso; e so para a saida deixar
    -- visivel se o GRANT foi parar no papel errado, que era o defeito do 056 antigo.
    RAISE NOTICE 'papel deste banco: % | o outro papel (%) tem SELECT? %',
        papel, outro,
        has_table_privilege(outro, 'homologacao.entrada_mp_p5_exclusividade_item_pedido', 'SELECT');

    IF falhas > 0 THEN
        RAISE EXCEPTION 'FALHOU: % permissao(oes) faltando para %.', falhas, papel;
    END IF;
    RAISE NOTICE 'OK: % tem SELECT e INSERT.', papel;
END
$permissoes$;

\echo '== Fim da VALIDACAO. Nada foi escrito. =='
