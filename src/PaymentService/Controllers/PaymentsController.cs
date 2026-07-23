using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Events;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private static readonly List<PaymentRecord> _payments = new();
    private static readonly object _lock = new();

    [HttpPost]
    public IActionResult RecordPayment([FromBody] PaymentRecordRequest request)
    {
        var record = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            Status = request.Status,
            TransactionId = request.TransactionId,
            OccurredAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _payments.Add(record);
        }

        return CreatedAtAction(nameof(GetPayment), new { id = record.Id }, record);
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetPayment(Guid id)
    {
        lock (_lock)
        {
            var payment = _payments.FirstOrDefault(p => p.Id == id);
            if (payment is null)
                return NotFound();

            return Ok(payment);
        }
    }

    [HttpGet("order/{orderId:guid}")]
    public IActionResult GetPaymentsByOrder(Guid orderId)
    {
        lock (_lock)
        {
            var payments = _payments.Where(p => p.OrderId == orderId).ToList();
            return Ok(payments);
        }
    }
}

#region dto
public class PaymentRecordRequest
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
}

public class PaymentRecord
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
#endregion