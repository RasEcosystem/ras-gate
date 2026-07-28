# RasGate

RasGate — небольшой HTTP-адаптер для утилиты администрирования `rac` платформы 1С:Предприятие.

Клиент передаёт аргументы RAC через HTTP, а RasGate запускает только настроенный исполняемый файл без использования shell и возвращает `stdout`, `stderr`, код завершения и время выполнения.

```text
HTTP-клиент → RasGate → RAC → RAS → кластер 1С
```

RasGate не интерпретирует вывод RAC и не предоставляет предметные API для кластеров, информационных баз или сеансов. Это контролируемый транспорт для команд RAC.

## Возможности

- запуск настроенного исполняемого файла `rac`;
- безопасная передача аргументов без shell;
- раздельное чтение `stdout` и `stderr`;
- таймаут и отмена выполнения;
- ограничение количества одновременно запущенных процессов;
- ограничение размера каждого выходного потока;
- защита выполнения команд API-ключом;
- endpoint’ы состояния RasGate и RAC;
- OpenAPI и Swagger UI в окружении `Development`.

## Требования

Для запуска готовой сборки необходимы:

- Windows или Linux;
- установленная утилита RAC;
- доступный сервер администрирования RAS.

Релизные сборки являются self-contained и не требуют отдельной установки .NET Runtime.

Для сборки из исходников необходим .NET SDK 10. Для команд из `Makefile` также требуется GNU Make.

## Быстрый старт

Настройте `appsettings.json`, расположенный рядом с исполняемым файлом RasGate:

```json
{
  "Urls": "http://127.0.0.1:5050",
  "RasGate": {
    "InstanceName": "RasGate Application",
    "ApiKey": "replace-with-your-secret-key"
  },
  "Rac": {
    "ExecutablePath": "/opt/1cv8/x86_64/rac",
    "TimeoutSeconds": 30,
    "MaxConcurrentProcesses": 4,
    "MaxOutputBytes": 4194304
  }
}
```

Для Windows путь к RAC записывается с экранированными обратными слешами:

```json
"ExecutablePath": "C:\\Program Files\\1cv8\\8.3.27.2214\\bin\\rac.exe"
```

Обязательно замените пример API-ключа собственным секретным значением.

Запустите RasGate:

```powershell
.\RasGate.Web.exe
```

```bash
chmod +x ./RasGate.Web
./RasGate.Web
```

Проверьте состояние:

```bash
curl http://127.0.0.1:5050/rasgate/status
curl http://127.0.0.1:5050/rac/status
```

Выполните безопасную команду `rac --version`:

```bash
curl \
  --request POST \
  --header "Content-Type: application/json" \
  --header "X-Api-Key: replace-with-your-secret-key" \
  --data '{"arguments":["--version"]}' \
  http://127.0.0.1:5050/rac/execute
```

По умолчанию сервис доступен только локально. Для удалённого доступа измените `Urls`, настройте сетевой экран и используйте HTTPS.

## Конфигурация

| Параметр | Назначение |
|---|---|
| `Urls` | Адреса и порты HTTP-сервиса |
| `RasGate:InstanceName` | Имя экземпляра в `/rasgate/status` |
| `RasGate:ApiKey` | Ключ для доступа к `/rac/execute` |
| `RasGate:Logging:IncludeQueryString` | Добавлять query string в журнал HTTP-запросов |
| `RasGate:Logging:IncludeRequestBody` | Добавлять поддерживаемые тела запросов в журнал |
| `RasGate:Logging:MaxRequestBodyBytes` | Максимальное количество байтов тела запроса в журнале |
| `Rac:ExecutablePath` | Абсолютный путь к `rac` |
| `Rac:TimeoutSeconds` | Таймаут выполнения команды |
| `Rac:MaxConcurrentProcesses` | Максимальное количество одновременно выполняемых процессов |
| `Rac:MaxOutputBytes` | Максимальный размер каждого из потоков `stdout` и `stderr` |

Параметры можно переопределять переменными окружения, заменяя `:` на `__`:

```bash
export RasGate__ApiKey="your-secret-key"
export Rac__TimeoutSeconds=60
```

## HTTP API

| Метод и путь | Авторизация | Назначение |
|---|---|---|
| `GET /rasgate/status` | Не требуется | Имя и версия RasGate |
| `GET /rac/status` | Не требуется | Доступность и версия RAC |
| `POST /rac/execute` | `X-Api-Key` | Выполнение команды RAC |

Тело запроса на выполнение:

```json
{
  "arguments": [
    "cluster",
    "list",
    "localhost:1545"
  ]
}
```

Успешный ответ:

```json
{
  "success": true,
  "data": {
    "exitCode": 0,
    "standardOutput": "...",
    "standardError": "",
    "durationMilliseconds": 42,
    "timedOut": false
  }
}
```

`success: true` означает, что RasGate успешно выполнил HTTP-запрос. Результат самой команды RAC определяется по `exitCode`.

Основные ошибки:

| HTTP-код | Код ошибки | Причина |
|---|---|---|
| `400` | `bad_request` или `validation_error` | Некорректный запрос |
| `401` | `unauthorized` | API-ключ отсутствует или неверен |
| `429` | `rac_capacity_exceeded` | Все слоты выполнения заняты |
| `502` | `rac_output_limit_exceeded` | `stdout` или `stderr` превысил лимит |
| `503` | `rac_unavailable` | Исполняемый файл RAC не удалось запустить |

Каждый API-ответ содержит заголовок `X-Trace-Id`, который можно использовать для сопоставления запроса с серверными логами.

## Безопасность

- задавайте API-ключ через переменную окружения или другое хранилище секретов;
- не передавайте ключ в query string и не сохраняйте его в системе контроля версий;
- используйте HTTPS при доступе через сеть;
- ограничивайте сетевой доступ к RasGate;
- учитывайте, что endpoint’ы состояния не требуют авторизации.

## Сборка и тестирование

```bash
make build
make test
make release
```

- `make build` — Release-сборка решения;
- `make test` — unit- и integration-тесты;
- `make release` — self-contained single-file архивы для Linux x64 и Windows x64.

Дополнительные материалы:

- [Нагрузочное и длительное тестирование](docs/load-testing.md);
- [Postman collection](postman/RasGate.postman_collection.json);
- [Postman environment](postman/RasGate.postman_environment.json).

## Лицензия

См. [LICENSE](LICENSE).
