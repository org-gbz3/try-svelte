# 0009. CodeQL の build-mode を none に統一する

## 状態

採用

## 背景

CodeQL ワークフローは build-mode を指定せず Autobuild ステップを使っていた。このため C# の解析では「build-mode が none でないため overlay-base データベースを作れない」という警告が毎回出ており、default ブランチでの overlay(差分)解析が使われていなかった。

また 2026-09-19 に Dependabot が `github/codeql-action` の init/autobuild/analyze を別々の PR で更新した。これらを順にマージしたとき、途中のコミットで init と analyze のバージョンが食い違い(`Loaded a configuration file for version '4.37.9', but running version '4.38.0'`)、CodeQL が configuration error で失敗した。

## 検討した選択肢

- 案A: C# も含め両言語を `build-mode: none` にする。長所: overlay 解析が有効になる。Autobuild ステップが不要になり、ビルド失敗で解析が止まらない。短所: C# はビルドせずに依存関係を推定して解析するため、生成コードなどの精度がビルドありより下がる可能性がある。
- 案B: C# は `build-mode: autobuild`(または manual)を明示し、JS/TS のみ `none` にする。長所: C# の解析精度を最大化できる。短所: C# の overlay 解析が引き続き無効で、警告も残る。

## 決定

案A を採用し、両言語とも `build-mode: none` とし、Autobuild ステップを削除する。あわせて Dependabot で `github/codeql-action*` をグループ化し、1つの PR で同時に更新する。

## 理由

backend は通常の ASP.NET Core プロジェクトで、ソース生成に依存する解析対象が少なく、buildless でも実用上十分な精度が見込めるため。ビルド環境の差異で解析が失敗するリスクも減らせる。

## 影響

- `Setup .NET` ステップは buildless 解析での依存関係解決のため残す。
- C# の解析結果で見落としや誤検知が増えた場合は、C# のみ `autobuild` または `manual` に戻すことを検討する。
