# Sample Fulfilment Inputs

All samples target `POST /api/fulfilments`.

---

## 2-line order

```json
{
  "fulfilmentId": "11111111-0000-0000-0000-000000000001",
  "customerId": "CUST-001",
  "orderId": "ORD-2LINE-001",
  "lines": [
    { "orderNo": "ORD-2LINE-001", "sku": "SKU-ALPHA-001", "quantity": 2, "unitOfMeasure": "EA" },
    { "orderNo": "ORD-2LINE-001", "sku": "SKU-BETA-002",  "quantity": 5, "unitOfMeasure": "EA" }
  ]
}
```

---

## 3-line order

```json
{
  "fulfilmentId": "22222222-0000-0000-0000-000000000002",
  "customerId": "CUST-002",
  "orderId": "ORD-3LINE-001",
  "lines": [
    { "orderNo": "ORD-3LINE-001", "sku": "SKU-ALPHA-001", "quantity": 1,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-3LINE-001", "sku": "SKU-BETA-002",  "quantity": 3,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-3LINE-001", "sku": "SKU-GAMMA-003", "quantity": 10, "unitOfMeasure": "BOX" }
  ]
}
```

---

## 4-line order

```json
{
  "fulfilmentId": "33333333-0000-0000-0000-000000000003",
  "customerId": "CUST-003",
  "orderId": "ORD-4LINE-001",
  "lines": [
    { "orderNo": "ORD-4LINE-001", "sku": "SKU-ALPHA-001", "quantity": 2,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-4LINE-001", "sku": "SKU-BETA-002",  "quantity": 4,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-4LINE-001", "sku": "SKU-GAMMA-003", "quantity": 1,  "unitOfMeasure": "BOX" },
    { "orderNo": "ORD-4LINE-001", "sku": "SKU-DELTA-004", "quantity": 20, "unitOfMeasure": "EA" }
  ]
}
```

---

## 6-line order

```json
{
  "fulfilmentId": "44444444-0000-0000-0000-000000000004",
  "customerId": "CUST-004",
  "orderId": "ORD-6LINE-001",
  "lines": [
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-ALPHA-001", "quantity": 2,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-BETA-002",  "quantity": 4,  "unitOfMeasure": "EA" },
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-GAMMA-003", "quantity": 1,  "unitOfMeasure": "BOX" },
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-DELTA-004", "quantity": 20, "unitOfMeasure": "EA" },
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-EPSILON-005", "quantity": 7, "unitOfMeasure": "EA" },
    { "orderNo": "ORD-6LINE-001", "sku": "SKU-ZETA-006",  "quantity": 3,  "unitOfMeasure": "BOX" }
  ]
}
```
