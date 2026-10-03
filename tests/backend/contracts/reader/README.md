# Reader contract fixtures (v1)

Example bodies of the photo reader's HTTP contract. The reader's tests check that it writes exactly this shape; the main app's
adapter tests (later) use them as canned reader answers, so both sides fail when the contract drifts.

- `read-response.*.json`: `POST /v1/read` answers (field values are invariant strings; `source` is `ocr`, `derived` or `hint`).
- `health.json`: `GET /v1/health` when the OCR is available.
- `error.json`: the body of every error status (`bad_request`, `bad_image`, `unauthorized`, `too_large`, `unsupported_type`,
  `busy`, `ocr_unavailable`).

No pictures live here: tests that need one draw it with the reader's generator (`Tankstat.Reader.Synthetic`), and real receipts or
dashboards are never committed.
