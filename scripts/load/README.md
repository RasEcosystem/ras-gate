# Нагрузочное тестирование RasGate

RasGate поддерживает два режима нагрузочного тестирования:

- полностью контейнерный запуск RasGate и k6 через Docker Compose;
- локальный запуск k6 на Linux против удалённого RasGate, например на Windows Server.

Во втором режиме Docker на Windows Server не требуется:

```text
Linux с k6 → HTTP/HTTPS → RasGate на Windows Server → RAC → RAS
```

## Сценарии

Сценарии, вспомогательный код и контейнерное окружение расположены в каталоге `scripts/load`.

| Сценарий | Назначение | Настройки по умолчанию |
|---|---|---|
| `smoke` | Проверка статуса RasGate, доступности RAC и одного выполнения команды | 1 итерация |
| `load` | Стабильная рабочая нагрузка без ожидаемых отказов | 4 VU, 2 минуты |
| `stress` | Рост нагрузки и проверка ограничения конкурентных процессов | 4 → 8 → 16 VU, 5 минут |
| `soak` | Длительная проверка стабильности и утечек ресурсов | 2 VU, 30 минут |

По умолчанию сценарии выполняют безопасную команду:

```json
["--version"]
```

## Результаты и логи

k6 выводит результаты удалённого теста в локальный Linux-терминал. Режим запуска не влияет на состав метрик.

По умолчанию итоговая сводка не сохраняется после закрытия терминала.

В итоговой сводке отображаются:

- успешные и неуспешные `checks`;
- выполнение заданных `thresholds`;
- количество HTTP-запросов и итераций;
- `http_req_duration` с перцентилями;
- `http_req_failed`;
- количество и активность VU;
- `rasgate_execution_errors`;
- `rasgate_capacity_rejections` для ответов `429`.

Итоговую сводку можно сохранить в JSON:

```bash
mkdir -p artifacts

SUMMARY_EXPORT=artifacts/k6-smoke.json \
make load-remote \
  TEST=smoke \
  BASE_URL=http://192.168.1.10:5050
```

Полный терминальный вывод можно одновременно сохранить в текстовый файл:

```bash
make load-remote \
  TEST=smoke \
  BASE_URL=http://192.168.1.10:5050 \
  2>&1 | tee artifacts/k6-smoke.log
```

Локальный k6 не получает файловые и консольные логи самого RasGate. Они остаются на Windows Server:

- `logs/requests` — журнал HTTP-запросов;
- `logs/errors` — ошибки RasGate и RAC.

Для ручной диагностики отдельного ответа и сопоставления его с серверным событием можно использовать время запроса и заголовок `X-Trace-Id`, возвращаемый RasGate.

## Контейнерный запуск

Перед запуском задайте параметры тестового экземпляра:

```bash
export RASGATE_INSTANCE_NAME="RasGate load test"
export RASGATE_API_KEY="replace-with-a-secret-api-key"
export RAC_HOST_PATH="/opt/1cv8/x86_64"
```

`RAC_HOST_PATH` должен указывать на каталог, содержащий исполняемый файл `rac`.

Запуск сценариев:

```bash
make load-run TEST=smoke
make load-run TEST=load
make load-run TEST=stress
make load-run TEST=soak
```

Команда `load-run` собирает и запускает RasGate и k6 через Docker Compose. После завершения теста контейнеры удаляются.

## Локальный k6 против удалённого RasGate

На Linux должен быть установлен `k6` и доступен в `PATH`.

На Windows Server RasGate должен принимать внешние подключения. Адрес можно задать в `appsettings.json`:

```json
{
  "Urls": "http://0.0.0.0:5050"
}
```

Или перед запуском в PowerShell:

```powershell
$env:Urls = "http://0.0.0.0:5050"
.\RasGate.Web.exe
```

В Windows Firewall следует разрешить входящие подключения к выбранному порту только с адреса Linux-машины, на которой запускается k6. При передаче трафика через недоверенную сеть следует использовать HTTPS.

Перед тестом доступность RasGate можно проверить с Linux:

```bash
curl http://192.168.1.10:5050/rasgate/status
```

Полностью интерактивный запуск:

```bash
make load-remote
```

Команда запросит:

1. имя сценария;
2. URL удалённого RasGate;
3. API-ключ со скрытым вводом.

Сценарий и URL можно передать сразу:

```bash
make load-remote \
  TEST=smoke \
  BASE_URL=http://192.168.1.10:5050
```

В этом случае будет запрошен только API-ключ.

Полностью неинтерактивный запуск:

```bash
API_KEY="replace-with-a-secret-api-key" \
make load-remote \
  TEST=load \
  BASE_URL=http://192.168.1.10:5050
```

API-ключ можно предварительно экспортировать, чтобы не сохранять его в истории команд:

```bash
export API_KEY="replace-with-a-secret-api-key"

make load-remote \
  TEST=load \
  BASE_URL=http://192.168.1.10:5050
```

Тот же runner можно вызвать без Make:

```bash
./scripts/run-load-test.sh \
  smoke \
  http://192.168.1.10:5050
```

Команда `load-remote` не использует Docker и не запускает RasGate. Она только выполняет локальный k6 против указанного URL.

## Настройка сценариев

Сценарии `load` и `soak` можно настраивать переменными окружения:

```bash
TEST_VUS=4 \
TEST_DURATION=10m \
TEST_PAUSE_SECONDS=0.5 \
make load-remote \
  TEST=load \
  BASE_URL=http://192.168.1.10:5050
```

| Переменная | Назначение |
|---|---|
| `TEST_VUS` | Количество виртуальных пользователей для `load` и `soak` |
| `TEST_DURATION` | Продолжительность `load` и `soak`, например `10m` или `2h` |
| `TEST_PAUSE_SECONDS` | Пауза между выполнениями команды одним VU |
| `REQUEST_TIMEOUT` | Таймаут HTTP-запроса k6, по умолчанию `35s` |
| `STARTUP_ATTEMPTS` | Количество попыток дождаться запуска RasGate |
| `STARTUP_DELAY_SECONDS` | Пауза между попытками проверки готовности |
| `RAC_ARGUMENTS_JSON` | JSON-массив аргументов команды RAC |
| `SUMMARY_EXPORT` | Путь к JSON-файлу с итоговой сводкой локального запуска k6 |

Другую команду RAC можно указать явно:

```bash
RAC_ARGUMENTS_JSON='["cluster","list","localhost:1545"]' \
make load-remote \
  TEST=smoke \
  BASE_URL=http://192.168.1.10:5050
```

Для нагрузочных тестов следует использовать только команды, не изменяющие состояние кластера.

## Интерпретация ошибок

Сценарии `smoke`, `load` и `soak` считают ошибками:

- HTTP-ответ, отличный от `200`;
- повреждённый JSON или неправильный контракт ответа;
- таймаут выполнения;
- ненулевой код завершения RAC;
- ответ `429 Too Many Requests`.

Сценарий `stress` считает структурированный ответ `429` с кодом `rac_capacity_exceeded` ожидаемым поведением защиты от перегрузки. Другие ответы `429`, ошибки сервера и повреждённые ответы считаются ошибками теста.
