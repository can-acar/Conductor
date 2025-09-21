namespace ExampleWebApplication.Module;

public class ProductDb
{
    public List<Product> Products { get; set; } = new()
    {
        new Product { Id = 1, Name = "Laptop", Price = 1500, Category = "Electronics" },
        new Product { Id = 2, Name = "Phone", Price = 800, Category = "Electronics" },
        new Product { Id = 3, Name = "Book", Price = 25, Category = "Education" }
    };

    public async Task<List<Product>> GetProductsAsync()
    {
        await Task.Delay(100); // Simulate database delay
        return Products;
    }

    public async Task<Product?> GetProductByIdAsync(int id)
    {
        await Task.Delay(50);
        return Products.FirstOrDefault(p => p.Id == id);
    }
}