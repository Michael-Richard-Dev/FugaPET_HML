\set ON_ERROR_STOP on
-- ============================================================
-- 067_cadastro_etiqueta_caixa_Q_VALIDACAO_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 067 (Q) - VALIDACAO (SOMENTE LEITURA)
--
-- Confere o cadastro e, principalmente, confere a PERGUNTA QUE A TELA FAZ: etiqueta
-- ativa e do tipo CAIXA. E essa consulta que estava voltando vazia.
-- ============================================================

\echo '== 067 Q VALIDACAO =='

SELECT current_database() AS banco;

\echo '-- o modelo (esperado: 1 linha, 80x50 mm, 203 dpi, ativo)'
SELECT nome_modelo_etiqueta, versao, largura_mm, altura_mm, dpi, situacao_modelo_etiqueta
  FROM homologacao.modelo_etiqueta
 WHERE upper(btrim(nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5'));

\echo '-- a etiqueta (esperado: 1 linha, tipo CAIXA, ativa, apontando para o modelo)'
SELECT e.codigo_interno, e.nome_etiqueta, e.tipo_etiqueta, e.situacao_etiqueta,
       m.nome_modelo_etiqueta AS modelo
  FROM homologacao.etiqueta e
  JOIN homologacao.modelo_etiqueta m ON m.codigo_modelo_etiqueta = e.codigo_modelo_etiqueta
 WHERE upper(btrim(e.codigo_interno)) = upper(btrim('CAIXA-PA-8X5'));

\echo '-- A PERGUNTA DA TELA: quantas etiquetas ativas do tipo CAIXA (esperado: 1)'
SELECT count(*) AS caixa_ativas
  FROM homologacao.etiqueta
 WHERE tipo_etiqueta = 'CAIXA' AND situacao_etiqueta;

\echo '-- nenhum vinculo foi criado por este incremento (esperado: 0)'
SELECT count(*) AS vinculos FROM homologacao.produto_etiqueta_vinculo;

\echo '-- rodar de novo nao duplica: as chaves unicas reais (esperado: 1 e 1)'
SELECT
    (SELECT count(*) FROM homologacao.modelo_etiqueta
      WHERE upper(btrim(nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5')) AND versao = 1) AS modelos,
    (SELECT count(*) FROM homologacao.etiqueta
      WHERE upper(btrim(codigo_interno)) = upper(btrim('CAIXA-PA-8X5'))) AS etiquetas;
