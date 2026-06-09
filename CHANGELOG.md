# [9.0.0-alpha.19](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.18...v9.0.0-alpha.19) (2026-06-09)


### Bug Fixes

* **deps:** bump camunda-schema-bundler to 2.4.3 ([197c422](https://github.com/camunda/orchestration-cluster-api-csharp/commit/197c422196f9514c9b247a4651a63d510b34e703))
* **deps:** bump camunda-schema-bundler to 2.4.3 ([1696cff](https://github.com/camunda/orchestration-cluster-api-csharp/commit/1696cffe162284c8bfae768d8bacb140b93bc26f))
* **gen:** regenerate artifacts ([eecaa06](https://github.com/camunda/orchestration-cluster-api-csharp/commit/eecaa06ff1b6a25566347b097afa4fcb00617355))
* widen integer-backed branded structs to long for ICamundaLongKey contract ([73f42ca](https://github.com/camunda/orchestration-cluster-api-csharp/commit/73f42ca8e78ad0922e833816ad5f907278043a86))

# [9.0.0-alpha.18](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.17...v9.0.0-alpha.18) (2026-06-04)


### Bug Fixes

* pass author-association to community notification workflow ([e633de5](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e633de55903e21be3c7dc5c02beaba4e72ac785d))

# [9.0.0-alpha.17](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.16...v9.0.0-alpha.17) (2026-06-04)


### Bug Fixes

* **gen:** regenerate artifacts ([f248e56](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f248e56d75ec31051e6ce55406cf803d22170d61))


### Features

* add Slack notifications for release failures and community events ([d945e1c](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d945e1cab99bab4b08f1d632e9658ee94bcef327))
* add Slack notifications for release failures and community events ([b5b8a10](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b5b8a10bfcf3e5e348d9c834394288309b6eaaa7))

# [9.0.0-alpha.16](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.15...v9.0.0-alpha.16) (2026-05-18)


### Bug Fixes

* **gen:** regenerate artifacts ([8d9b85a](https://github.com/camunda/orchestration-cluster-api-csharp/commit/8d9b85af4260079fbb02c5be639c67c40c49f278))
* harden release pipeline against unreviewed file staging ([e518c39](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e518c39d88571dabc34dc14c8219ed4b65da2cb5))
* use git add -f for gitignored spec artifacts in release ([d12cddf](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d12cddf1790ace4eeade8a79940a12383bed908c))
* use git add -f for gitignored spec artifacts in release ([58381f6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/58381f6c9af5ed2e52c003c60d0fbe7d7e735fbe))

# [9.0.0-alpha.15](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.14...v9.0.0-alpha.15) (2026-05-15)


### Bug Fixes

* **gen:** regenerate artifacts ([76c4614](https://github.com/camunda/orchestration-cluster-api-csharp/commit/76c4614e17573458ba7b11c66d91e5720dd9fe08))
* sanitize remaining spec-controlled strings in generator output ([e1bbe6d](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e1bbe6d8cf5fb640cc8644c379fcd989a33893a3))
* sanitize remaining spec-controlled strings in generator output ([6e9ba17](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6e9ba17ae54fecefe497b92ba1e8645d7e19e266)), closes [camunda/security-testing-findings#65](https://github.com/camunda/security-testing-findings/issues/65)

# [9.0.0-alpha.14](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.13...v9.0.0-alpha.14) (2026-05-13)


### Bug Fixes

* **gen:** regenerate artifacts ([fdd209c](https://github.com/camunda/orchestration-cluster-api-csharp/commit/fdd209cd82f9554d85608ec2a3c82caa7c99f1d9))
* resolve $ref schemas for binary detection and add SendBinaryAsync tests ([f8b0af8](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f8b0af8ab5367c8e4643f40a4f688da3a66085d6))
* return byte[] for binary (octet-stream) response operations ([586a926](https://github.com/camunda/orchestration-cluster-api-csharp/commit/586a9262b2b12c7cb1f99c54279bcd32129f010a))
* return byte[] for binary (octet-stream) response operations ([e5aaaa2](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e5aaaa2e199bdbdcf0ac7f19bffed9df27f43759))


### Features

* add SDK examples for new operations ([824564d](https://github.com/camunda/orchestration-cluster-api-csharp/commit/824564d35285292eeb048423522e1c4419e0c7a4))

# [9.0.0-alpha.13](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.12...v9.0.0-alpha.13) (2026-05-11)


### Bug Fixes

* **gen:** regenerate artifacts ([6b06ee4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6b06ee4cf342b0304c9a7fe93c3a810459142ae3))
* map bare type:object schemas to object instead of empty class ([9eb35d6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/9eb35d6b18723785673c2d61993ca60339dc871d))
* map bare type:object schemas to object instead of empty class ([2512e09](https://github.com/camunda/orchestration-cluster-api-csharp/commit/2512e0945479397e40b3fc3cfa5c691fba8b7810))

# [9.0.0-alpha.12](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.11...v9.0.0-alpha.12) (2026-05-08)


### Bug Fixes

* **gen:** regenerate artifacts ([d23f0cf](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d23f0cf1cd375d5feab7d6b9c61de8ba1395028b))


### Features

* generate typed enums for inline string enum properties ([6721d7d](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6721d7d31906ad94e9e217200e9f7b569488091a))
* generate typed enums for inline string enum properties ([69d7086](https://github.com/camunda/orchestration-cluster-api-csharp/commit/69d7086f7a77360dd1535a4216b6752af7da4bc4)), closes [#171](https://github.com/camunda/orchestration-cluster-api-csharp/issues/171)

# [9.0.0-alpha.11](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.10...v9.0.0-alpha.11) (2026-05-08)


### Bug Fixes

* update examples to use branded type parameters ([47a7d1a](https://github.com/camunda/orchestration-cluster-api-csharp/commit/47a7d1a0132a2a1376de69ce8837bd94f02a2d80))


### Features

* add examples for getAgentInstance and searchAgentInstances ([4aadc17](https://github.com/camunda/orchestration-cluster-api-csharp/commit/4aadc1739c33e66b48602c5796ff71cfe3bf5828))
* upgrade bundler to 2.4.1 and update default spec ref to main ([86ad8c6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/86ad8c6b4098faef73cd62b1706d7400387d2e82))

# [9.0.0-alpha.10](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.9...v9.0.0-alpha.10) (2026-05-05)


### Bug Fixes

* Potential fix for pull request finding ([b3f6353](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b3f6353a3bd047418d852b0cd16d08587f519043))


### Features

* **worker:** support TenantIds and TenantId on JobWorkerConfig ([f34852b](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f34852bd80a735ee7985d115caeff4891e0ed39d))
* **worker:** support TenantIds and TenantId on JobWorkerConfig ([bf24581](https://github.com/camunda/orchestration-cluster-api-csharp/commit/bf245814877b5bf893ce18cb6817dab0496b34ae)), closes [#120](https://github.com/camunda/orchestration-cluster-api-csharp/issues/120)

# [9.0.0-alpha.9](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.8...v9.0.0-alpha.9) (2026-05-04)


### Bug Fixes

* **gen:** regenerate artifacts ([8da4ae8](https://github.com/camunda/orchestration-cluster-api-csharp/commit/8da4ae85a1a4cf9ff0174b80ffa166901d36002f))

# [9.0.0-alpha.8](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.7...v9.0.0-alpha.8) (2026-04-30)


### Bug Fixes

* **gen:** regenerate artifacts ([d8f54bc](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d8f54bcb544c2752be6a567b0c10db4c00259d79))

# [9.0.0-alpha.7](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.6...v9.0.0-alpha.7) (2026-04-29)


### Bug Fixes

* **gen:** apply CAMUNDA_DEFAULT_TENANT_ID to ActivateJobsAsync tenantIds ([#123](https://github.com/camunda/orchestration-cluster-api-csharp/issues/123)) ([b6536ff](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b6536ffbc83b27068476406ee8deea7de048780c))
* **gen:** regenerate artifacts ([de541d3](https://github.com/camunda/orchestration-cluster-api-csharp/commit/de541d3fe3ad8b8ae809498c0ee8836f59783ea2))

# [9.0.0-alpha.6](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.5...v9.0.0-alpha.6) (2026-04-29)


### Bug Fixes

* **gen:** regenerate artifacts ([de9b79f](https://github.com/camunda/orchestration-cluster-api-csharp/commit/de9b79f59a328b97636154b80ef3525252acca92))

# [9.0.0-alpha.5](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.4...v9.0.0-alpha.5) (2026-04-23)


### Bug Fixes

* **generator:** sanitize spec-controlled strings against Unicode injection ([#116](https://github.com/camunda/orchestration-cluster-api-csharp/issues/116)) ([705b472](https://github.com/camunda/orchestration-cluster-api-csharp/commit/705b4728da94c53e5ac02f1734e1c78b16e680d9))

# [9.0.0-alpha.4](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.3...v9.0.0-alpha.4) (2026-04-23)


### Bug Fixes

* **gen:** regenerate artifacts ([140b000](https://github.com/camunda/orchestration-cluster-api-csharp/commit/140b000b44443a223bb294e9176642a67adb8d6a))

# [9.0.0-alpha.3](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.2...v9.0.0-alpha.3) (2026-04-14)


### Bug Fixes

* disable successComment to avoid GraphQL error on non-PR commits ([77de048](https://github.com/camunda/orchestration-cluster-api-csharp/commit/77de048e80fa438fb94afb2744f16ab6d5f6b7dd))


### Features

* begin SDK 10 development for Camunda server 8.10 ([5f61d22](https://github.com/camunda/orchestration-cluster-api-csharp/commit/5f61d22eaca7f74e81846bda22a703f35f33ce4f))


### BREAKING CHANGES

* SDK major version bumped from 9 to 10

# [9.0.0-alpha.2](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v9.0.0-alpha.1...v9.0.0-alpha.2) (2026-04-14)


### Bug Fixes

* update release process ([d559644](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d559644cf93b53eb0ee2dfef086d7c6fa1ff00fe))

# [9.0.0-alpha.1](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.38...v9.0.0-alpha.1) (2026-04-14)


### Features

* release SDK 9 for Camunda server 8.9 ([2008077](https://github.com/camunda/orchestration-cluster-api-csharp/commit/200807767858f471904ffd681d57c299fa7df281))


### BREAKING CHANGES

* SDK major version bumped from 8 to 9 to track Camunda server 8.9

# [8.9.0-alpha.38](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.37...v8.9.0-alpha.38) (2026-04-14)


### Bug Fixes

* update release process documentation ([eafeb4d](https://github.com/camunda/orchestration-cluster-api-csharp/commit/eafeb4dce5ca58806014de88f6aa2933bccb8f02))

# [8.9.0-alpha.37](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.36...v8.9.0-alpha.37) (2026-04-09)


### Bug Fixes

* **gen:** regenerate artifacts ([d3764d0](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d3764d08eee015f7393064597dad14ea3b846828))
* prefer DeployResourcesFromFiles in API spec examples ([965e492](https://github.com/camunda/orchestration-cluster-api-csharp/commit/965e4929a710eefd0b7249cdf65bd93c2d3cdd5d))

# [8.9.0-alpha.36](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.35...v8.9.0-alpha.36) (2026-04-09)


### Bug Fixes

* add missing overwrite entries and examples (green) ([f498d85](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f498d859b25e914b20482ee2d669b5d90cb473f6)), closes [#78](https://github.com/camunda/orchestration-cluster-api-csharp/issues/78)
* address PR review comments ([6efa8b4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6efa8b47886b81a16225ac896d43a78616a49b61))

# [8.9.0-alpha.35](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.34...v8.9.0-alpha.35) (2026-04-09)


### Bug Fixes

* flatten CamundaClient TOC in API docs to alphabetical list ([c086bcd](https://github.com/camunda/orchestration-cluster-api-csharp/commit/c086bcd77688c1bf70ab12d1f9199cd17b4ddc4e))

# [8.9.0-alpha.34](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.33...v8.9.0-alpha.34) (2026-04-09)


### Bug Fixes

* **gen:** regenerate artifacts ([0e01fdd](https://github.com/camunda/orchestration-cluster-api-csharp/commit/0e01fddb10c68aa93c5fc372a96eb6a1ec3c6221))
* separate code blocks per example in IntelliSense remarks ([397c2b4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/397c2b4dd4d451358004c1d342f7d082bc17e6f8))


### Features

* include code examples in remarks for IntelliSense ([0039de7](https://github.com/camunda/orchestration-cluster-api-csharp/commit/0039de735d3730d94fd44c4a8a69491c4da72b92))

# [8.9.0-alpha.33](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.32...v8.9.0-alpha.33) (2026-04-08)


### Bug Fixes

* shorten discriminant labels for multi-entry operations ([e7b0ec2](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e7b0ec2236822c53f312a6d874adcfd41a703390))


### Features

* add imports field to operation-map entries ([afcc895](https://github.com/camunda/orchestration-cluster-api-csharp/commit/afcc89567057cd307c49b610be573a513f72ab8b))

# [8.9.0-alpha.32](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.31...v8.9.0-alpha.32) (2026-04-07)


### Bug Fixes

* add validation gate for overwrite example resolution ([09cb254](https://github.com/camunda/orchestration-cluster-api-csharp/commit/09cb254bc741b3a9e4c9b1f32a030224e0a4cf5e))
* point docs-md generator at correct examples directory ([bb3c9de](https://github.com/camunda/orchestration-cluster-api-csharp/commit/bb3c9de4352b296f9e494d77af35efcfc98a5903))

# [8.9.0-alpha.31](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.30...v8.9.0-alpha.31) (2026-04-06)


### Bug Fixes

* handle YAML value tag in DocFX output ([9aecbad](https://github.com/camunda/orchestration-cluster-api-csharp/commit/9aecbad77326e6f7a390712e05bd3d98cfa4cce2))

# [8.9.0-alpha.30](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.29...v8.9.0-alpha.30) (2026-04-03)


### Bug Fixes

* sort required query params before optional in generator ([b88b5cf](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b88b5cf506c1244bc0c86eda59d791b61f7fcb65)), closes [#62](https://github.com/camunda/orchestration-cluster-api-csharp/issues/62)

# [8.9.0-alpha.29](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.28...v8.9.0-alpha.29) (2026-04-03)


### Bug Fixes

* **gen:** regenerate artifacts ([877832b](https://github.com/camunda/orchestration-cluster-api-csharp/commit/877832bcfee413fdf4240dace9029851bcacdf18))

# [8.9.0-alpha.28](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.27...v8.9.0-alpha.28) (2026-04-03)


### Features

* add Roslyn-based API changelog tool ([d68fb52](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d68fb524844052e8168ba3cbb239e9285b94bb06))

# [8.9.0-alpha.27](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.26...v8.9.0-alpha.27) (2026-04-02)


### Bug Fixes

* address review comments on TLS handler ([20d778a](https://github.com/camunda/orchestration-cluster-api-csharp/commit/20d778a0f2b5ef36d330ee59352b15e76c84ebbb))
* **gen:** regenerate artifacts ([30433b1](https://github.com/camunda/orchestration-cluster-api-csharp/commit/30433b15de707184e28ffe0ef64f46a090b85e52))


### Features

* support custom TLS certificates (self-signed CA & mTLS) ([617f856](https://github.com/camunda/orchestration-cluster-api-csharp/commit/617f85686fb6fef986dc727cc9f3b5314c09822c)), closes [#54](https://github.com/camunda/orchestration-cluster-api-csharp/issues/54)

# [8.9.0-alpha.26](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.25...v8.9.0-alpha.26) (2026-04-02)


### Bug Fixes

* **gen:** regenerate artifacts ([ce960bb](https://github.com/camunda/orchestration-cluster-api-csharp/commit/ce960bbbf9ae0a7332d521cb6e941f84d2f5962a))

# [8.9.0-alpha.25](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.24...v8.9.0-alpha.25) (2026-04-02)


### Features

* inject code examples into generated SDK method XML docs ([740522b](https://github.com/camunda/orchestration-cluster-api-csharp/commit/740522b6c11f3a2aade13135b768771a7a4e2980)), closes [#53](https://github.com/camunda/orchestration-cluster-api-csharp/issues/53)

# [8.9.0-alpha.24](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.23...v8.9.0-alpha.24) (2026-04-02)


### Bug Fixes

* **gen:** regenerate artifacts ([2b5d47a](https://github.com/camunda/orchestration-cluster-api-csharp/commit/2b5d47ac1e17122a2ee3481bd39ddcae82a21062))
* make build warnings fatal, suppress known example warnings ([2513561](https://github.com/camunda/orchestration-cluster-api-csharp/commit/251356133267eda05ec2f5aa349bd1787106aac2)), closes [#49](https://github.com/camunda/orchestration-cluster-api-csharp/issues/49)
* skip discriminator property on derived types for .NET 10 compat ([d8d1495](https://github.com/camunda/orchestration-cluster-api-csharp/commit/d8d14955b4d32d737d186b889e8e6bf36d526d39)), closes [#38](https://github.com/camunda/orchestration-cluster-api-csharp/issues/38)

# [8.9.0-alpha.23](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.22...v8.9.0-alpha.23) (2026-04-01)


### Bug Fixes

* address PR review comments ([fc2b910](https://github.com/camunda/orchestration-cluster-api-csharp/commit/fc2b9103821b616f496a7c1dccca4e20784ddb28))
* **gen:** regenerate artifacts ([75e13b1](https://github.com/camunda/orchestration-cluster-api-csharp/commit/75e13b1719d249a2bfc2d1eb0b7dc3e03266edcc))
* group document operations together in operation-map ([874a5c6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/874a5c6ec4bae62891abf2f2f648d4f20b5e8a42))

# [8.9.0-alpha.22](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.21...v8.9.0-alpha.22) (2026-04-01)


### Bug Fixes

* inject default tenant ID for multipart createDeployment ([f24e052](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f24e052680dfa46569180809e375f1fc6d072b21)), closes [#40](https://github.com/camunda/orchestration-cluster-api-csharp/issues/40)


### Features

* embed specHash from spec-metadata.json in published package ([0db4ba5](https://github.com/camunda/orchestration-cluster-api-csharp/commit/0db4ba58386c4bcf161e792e8dc1461cf5e8a807)), closes [#39](https://github.com/camunda/orchestration-cluster-api-csharp/issues/39)

# [8.9.0-alpha.21](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.20...v8.9.0-alpha.21) (2026-03-31)


### Bug Fixes

* **gen:** regenerate artifacts ([a0ea912](https://github.com/camunda/orchestration-cluster-api-csharp/commit/a0ea9127431fc4def5511e404a084f33b4920c48))

# [8.9.0-alpha.20](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.19...v8.9.0-alpha.20) (2026-03-31)


### Bug Fixes

* build from stable/8.9 ([bc5b929](https://github.com/camunda/orchestration-cluster-api-csharp/commit/bc5b9298057ea1a568d9858fa6ea347d4a55f958))

# [8.9.0-alpha.19](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.18...v8.9.0-alpha.19) (2026-03-31)


### Bug Fixes

* harden README code injection ([6965ad6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6965ad653a9dbc725506a22afc0e6c1716965bec)), closes [#36](https://github.com/camunda/orchestration-cluster-api-csharp/issues/36)

# [8.9.0-alpha.18](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.17...v8.9.0-alpha.18) (2026-03-27)


### Bug Fixes

* **gen:** regenerate artifacts ([6a4276e](https://github.com/camunda/orchestration-cluster-api-csharp/commit/6a4276e0fcd2293604939517c9c507d297738a7f))


### Features

* heritable worker defaults via CAMUNDA_WORKER_* env vars ([3233cd7](https://github.com/camunda/orchestration-cluster-api-csharp/commit/3233cd704a1cb7be2f7c38be7a7f4d6f509b73f2))
* support job corrections and denial in worker handlers ([f19a8bf](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f19a8bf41fd1ea83e0600820ad768975c103ef0e))

# [8.9.0-alpha.17](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.16...v8.9.0-alpha.17) (2026-03-25)


### Bug Fixes

* add IAsyncDisposable, diagnostic logging, and 30 new tests ([0b1a497](https://github.com/camunda/orchestration-cluster-api-csharp/commit/0b1a497a99b4ea9b28cff909ffe655980df926a2))
* always throw on non-2xx HTTP responses ([36b0135](https://github.com/camunda/orchestration-cluster-api-csharp/commit/36b0135980d0088f1e995c0ac72f3c7810112631))
* remove unused Polly dependency ([b60ad0f](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b60ad0f116a46253fcf6904ce1d59dd5891cd2ee))


### Features

* generate FilterProperty classes with advanced filter properties ([009b6df](https://github.com/camunda/orchestration-cluster-api-csharp/commit/009b6dffb6dcf61ba1adc14d09a98767be12680f))

# [8.9.0-alpha.16](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.15...v8.9.0-alpha.16) (2026-03-25)


### Bug Fixes

* add GroupId to CreateGroupRequest example ([8e6b004](https://github.com/camunda/orchestration-cluster-api-csharp/commit/8e6b004826cf0999b148db286d4fed81690f38de))
* resolve PR review comments - add XML region tags, compile examples, fix types ([4e3136c](https://github.com/camunda/orchestration-cluster-api-csharp/commit/4e3136c364b3a9d38585a00c435091f079e909e1)), closes [#region](https://github.com/camunda/orchestration-cluster-api-csharp/issues/region)

# [8.9.0-alpha.15](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.14...v8.9.0-alpha.15) (2026-03-24)


### Bug Fixes

* **gen:** regenerate artifacts ([341f8e2](https://github.com/camunda/orchestration-cluster-api-csharp/commit/341f8e2e2251ee6efc2a158237023e6848b1c95a))


### Features

* generate implicit conversions and equality for union semantic key types ([fd0f0c8](https://github.com/camunda/orchestration-cluster-api-csharp/commit/fd0f0c85ff57a9d68eefd86136a39b161f1c3973))

# [8.9.0-alpha.14](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.13...v8.9.0-alpha.14) (2026-03-24)


### Bug Fixes

* build for latest stable/8.9 ([be406c4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/be406c4d04be21e61747e8c1c2b403b10c91b1ce))
* **gen:** regenerate artifacts ([05eebb6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/05eebb6cdc66cb10ca5fce5321fb7bee71ba82a7))

# [8.9.0-alpha.13](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.12...v8.9.0-alpha.13) (2026-03-24)


### Bug Fixes

* **gen:** regenerate artifacts ([e5444ee](https://github.com/camunda/orchestration-cluster-api-csharp/commit/e5444eebe2695f4750d7f8d24a72ec82fabf2285))


### Features

* segment README into per-section Docusaurus pages ([8155e96](https://github.com/camunda/orchestration-cluster-api-csharp/commit/8155e966b97243b144c7d70e9f7ccf9d0fab2cc0)), closes [#9](https://github.com/camunda/orchestration-cluster-api-csharp/issues/9)

# [8.9.0-alpha.12](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.11...v8.9.0-alpha.12) (2026-03-23)


### Bug Fixes

* build from stable/8.9 ([45033a1](https://github.com/camunda/orchestration-cluster-api-csharp/commit/45033a16aa62bf479647f71dc4c8f8ccadae10d2))
* **gen:** regenerate artifacts ([948eb10](https://github.com/camunda/orchestration-cluster-api-csharp/commit/948eb10a6f890cd042529e07a501775e23429ca1))

# [8.9.0-alpha.11](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.10...v8.9.0-alpha.11) (2026-03-23)


### Bug Fixes

* address PR review comments ([4c0f313](https://github.com/camunda/orchestration-cluster-api-csharp/commit/4c0f3138a2ae83d9a6b228144903d2c449c714ad))

# [8.9.0-alpha.10](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.9...v8.9.0-alpha.10) (2026-03-20)


### Bug Fixes

* **gen:** regenerate artifacts ([4e514ca](https://github.com/camunda/orchestration-cluster-api-csharp/commit/4e514ca01b59de86ccf6b8cfa6fd62b1885c5ef1))
* **gen:** regenerate SDK from stable/8.9 spec with unified namespace ([7ff87b8](https://github.com/camunda/orchestration-cluster-api-csharp/commit/7ff87b8755e42ece111ab38c78eee087c0d330d2))

# [8.9.0-alpha.9](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.8...v8.9.0-alpha.9) (2026-03-20)


### Bug Fixes

* align config defaults with JS SDK for zero-config SaaS support ([b49146c](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b49146c617ddd87c2181eeb7a1ac87901301d2d9))
* correctly handle nullables ([fbf130c](https://github.com/camunda/orchestration-cluster-api-csharp/commit/fbf130c998be9b16027f50955b874665904d2e1e))
* deprecate some enum members ([f9626ca](https://github.com/camunda/orchestration-cluster-api-csharp/commit/f9626ca6acfd759a937636af09e2af02f15c3cfd))
* **gen:** regenerate artifacts ([85f9b10](https://github.com/camunda/orchestration-cluster-api-csharp/commit/85f9b10f88b343f11ff209b08236c808465b6b44))
* **gen:** regenerate artifacts ([77e8755](https://github.com/camunda/orchestration-cluster-api-csharp/commit/77e87556966019d8c9357b7a4ef538b678a0df95))
* **gen:** regenerate artifacts ([1e282c3](https://github.com/camunda/orchestration-cluster-api-csharp/commit/1e282c37a98d34e29fe27e04ffe4fd51874f2330))
* **gen:** regenerate artifacts ([3dcb2e4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/3dcb2e4da61b79a432784fd376ff15999290ef26))
* **gen:** regenerate artifacts ([671da1b](https://github.com/camunda/orchestration-cluster-api-csharp/commit/671da1b02333e93de69b32dce2c2c108f683ac1b))
* **gen:** regenerate artifacts ([9606881](https://github.com/camunda/orchestration-cluster-api-csharp/commit/96068812e706b78ad5e8b0c3f1d1987008e27789))
* **gen:** regenerate artifacts ([3a98fb1](https://github.com/camunda/orchestration-cluster-api-csharp/commit/3a98fb136b8a69000407dcd984b2573847a2b5a3))
* **gen:** regenerate artifacts ([0e75716](https://github.com/camunda/orchestration-cluster-api-csharp/commit/0e75716f2ea7b457d7cb7c6f1fab87090a5fb3fa))
* moved Camunda class func to CamundaClient to prevent namespace issues ([cc260d6](https://github.com/camunda/orchestration-cluster-api-csharp/commit/cc260d620139da91490f425b4a3c1ad5ef326910))


### Features

* add startup jitter to worker creation ([41b26aa](https://github.com/camunda/orchestration-cluster-api-csharp/commit/41b26aa1a3c521b22287f760906ef60ea5222363))
* build from latest stable/8.9 ([87fd01f](https://github.com/camunda/orchestration-cluster-api-csharp/commit/87fd01f0bee57cde10f6c5bf7a2cedf0efad0e5f))
* build from latest stable/8.9 ([786b090](https://github.com/camunda/orchestration-cluster-api-csharp/commit/786b090c900e685357dba9f32b89050de3728a78))
* emit JsonPolymorphic discriminator attributes for oneOf schemas ([b89bbf4](https://github.com/camunda/orchestration-cluster-api-csharp/commit/b89bbf4bbdd625f60741adc6db37c72da089cf66))
* support deprecated enum members ([a73006a](https://github.com/camunda/orchestration-cluster-api-csharp/commit/a73006a89a1a2c893299843735b6142bc27ed3bd))

# [8.9.0-alpha.8](https://github.com/camunda/orchestration-cluster-api-csharp/compare/v8.9.0-alpha.7...v8.9.0-alpha.8) (2026-02-17)


### Bug Fixes

* **gen:** regenerate artifacts ([2137ad1](https://github.com/camunda/orchestration-cluster-api-csharp/commit/2137ad1bb3e8269f12e04e74feb43ced15b854c3))
* use camunda-schema-bundler 1.3.3. **contains breaking type changes** ([6359740](https://github.com/camunda/orchestration-cluster-api-csharp/commit/63597400d5cda19ac040cf9ca2b0bbb5d43e9861))
