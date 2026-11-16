# Configuração operacional — linha Q (QAS110)

Levantado em 2026-11-12 a partir da instalação em `C:\FugaPET_QAS110`, que é a
referência em uso. Este documento existe porque os três `configuracao.*.json`
reais são `gitignored`: até aqui a configuração que faz o sistema funcionar
existia **apenas** na pasta publicada, e não se reconstruía do repositório.

**Nenhuma credencial é registrada aqui.** Usuário e senha vêm por variável de
ambiente (ver a última seção) ou são preenchidos no arquivo implantado.

## Como reconstruir a configuração

1. Copiar `configuracao.sap.exemplo.json` → `configuracao.sap.json`,
   `configuracao.banco.exemplo.json` → `configuracao.banco.json`,
   `configuracao.terminal.exemplo.json` → `configuracao.terminal.json`.
2. Substituir os endpoints pelos valores da tabela abaixo (os exemplos apontam
   para o host `vhfufds4ci`, não para o host de Q).
3. Preencher as credenciais por variável de ambiente.

O `configuracao.terminal.exemplo.json` já traz o mapeamento real de máquinas, e
o `configuracao.sap.exemplo.json` já traz as 25 chaves que o
`LeitorConfiguracaoSap` da linha Q lê, todas as flags de escrita em `false`.

## Endpoints SAP em uso no Q

Host S/4 de Q: `vhfufqs4ci.sap.fugacouros.com.br:44300` — `sap_client` `110`,
`timeout_segundos` `30`.

| Chave | Serviço |
|---|---|
| `base_url` | `API_MATERIAL_DOCUMENT_SRV/` |
| `material_document_base_url` | `API_MATERIAL_DOCUMENT_SRV/` |
| `purchase_order_base_url` | `odata4/.../api_purchaseorder_2/srvd_a2x/sap/purchaseorder/0001/` |
| `production_order_base_url` | `API_PRODUCTION_ORDER_2_SRV/` |
| `production_order_confirmation_base_url` | `API_PROD_ORDER_CONFIRMATION_2_SRV/` |
| `product_base_url` | `API_PRODUCT_SRV/` |
| `handling_unit_base_url` | `odata4/.../api_handlingunit/srvd_a2x/sap/handlingunit/0001/` |

`hosts_permitidos` = `["vhfufqs4ci.sap.fugacouros.com.br"]`.

### Integration Suite (CPI) — integração distinta do S/4

Host: `fuga-sub-integration-suite-3k7anqzs.it-cpi008-rt.cfapps.br10.hana.ondemand.com`,
`packaging_sap_client` `110`. Precisa constar em `packaging_hosts_permitidos` e
em `pallet_int012_hosts_permitidos`, que são allowlists **separadas**.

- `packaging_base_url` → `/http/QS4_110/pesagem/handling_unit/ZC_PP_PACKINGINSTRUCTION`
- `pallet_int012_base_url` → `/http/QS4_110/pesagem/handling_unit/CreateHUInput/1111/SAP__self.processHandlingUnitPayload`

`pallet_int012_base_url` nunca é derivada de `handling_unit_base_url`. Vazia, o
gateway INT012 fica fail-closed e não faz HTTP algum.

## Perfil de ambiente e flags de escrita

A instalação declara `"ambiente": { "perfil": "Q" }`. Com isso o EXE iniciado
por duplo clique liga por perfil, via `DefaultsPerfilQ`, as capabilities
`hu_write_habilitado`, `pa_pipeline_habilitado`,
`pa_material_document_write_enabled` e packaging — a resolução é
`valorArquivo || padraoPerfil`, então **`false` no arquivo não desliga nenhuma
delas sob perfil Q**; só uma variável de ambiente de Process com `false`
sobrepõe.

Duas escritas não participam do perfil e nenhum perfil consegue ligá-las:
`escrita_habilitada` (escrita SAP genérica) e `pallet_write_habilitado`
(escrita de palete INT012). Perfil ausente, vazio ou desconhecido = nenhuma
capability.

## Tara física da embalagem do palete (GATE 126V)

`taras_fisicas_embalagem_palete` em uso: `sap_client` `110`, `centro` `3007`,
`material_embalagem` `PALLET01`, `peso_kg` `1`, `unidade` `KG`.

Só entra na composição do payload INT012; não altera o palete local e não é
persistida. Ausência ou duas entradas para a mesma chave = fail-closed, e
ausência nunca é lida como tara zero.

## Terminais

| Máquina | Terminal | Banco local | Impressora (fila do Windows) | Setor | Balança | Etiqueta | Tara |
|---|---|---|---|---|---|---|---|
| `NT-TI-256` | `TERMINAL_TI_TESTE` | `LOCAL` | `Zebra Rede 200.97` | 1 | 1 | 1 | 1 |
| `TERM-03` | `TERMINAL_FRIGOSUL_03` | `LOCAL_FRIGOSUL_03` | `ZDesigner GK420t` | 1 | 1 | 1 | 1 |

`impressora_padrao` é o nome da fila instalada no Windows daquela máquina;
trocar a impressora física exige atualizar esse nome. A Zebra de rede usada nos
testes de etiqueta está em `192.168.200.97`.

## Credenciais — somente por variável de ambiente

| Variável | Uso |
|---|---|
| `FUGAPET_Q_SAP_USERNAME` / `FUGAPET_Q_SAP_PASSWORD` | S/4 de Q |
| `FUGAPET_SAP_USERNAME` / `FUGAPET_SAP_PASSWORD` | S/4 (nomes genéricos) |
| `FUGAPET_Q_SAP_PACKAGING_USERNAME` / `FUGAPET_Q_SAP_PACKAGING_PASSWORD` | norma de embalagem (CPI) — credenciais próprias, nunca as do S/4 |
| `FUGAPET_SAP_PALLET_INT012_USERNAME` / `FUGAPET_SAP_PALLET_INT012_PASSWORD` | CPI/INT012 do palete — sem fallback para as do S/4 |

A instalação atual em `C:\FugaPET_QAS110` traz `usuario` e `senha` dentro de
`configuracao.sap.json`. Esse arquivo não é versionado e não deve ser copiado
para o repositório.

## Divergência conhecida

O `configuracao.terminal.json` do fonte DEV ainda aponta
`ZDesigner ZT230-200dpi ZPL SERIAL`, enquanto a instalação publicada já usa
`Zebra Rede 200.97`. Os arquivos reais do fonte não foram alterados nesta
rodada — só os `.exemplo.json` rastreados e este documento.
