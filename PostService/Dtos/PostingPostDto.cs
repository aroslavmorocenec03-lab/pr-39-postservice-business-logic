using PostService.Models;

namespace PostService.Dtos;

// Клієнт не визначає Id, CreatedAt або Price нового запису.
public class PostingPostDto
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DeliveryType DeliveryType { get; set; }
    public float Weight { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public float Value { get; set; }
}
