using RepositoryAfter;
using RepositoryBefore;
using Xunit;

public class ProductTests
{
    [Fact]
    public void BeforeApp_Returns_AllProducts_FromMemory()
    {
        var products = ProductFunctions.GetProductsFromMemory();

        Assert.Equal(4, products.Count);
        Assert.Contains(products, product => product["name"]?.ToString() == "Laptop");
    }

    [Fact]
    public void AfterApp_Provides_FeaturedProducts_MatchingBeforeList()
    {
        var legacyFeatured = ProductFunctions.GetFeaturedProductsFromMemory();
        var catalog = new ProductCatalog();
        var refinedFeatured = catalog.GetFeaturedProducts();

        Assert.Equal(legacyFeatured.Count, refinedFeatured.Count);
        Assert.All(refinedFeatured, product => Assert.True(product.IsFeatured));
    }
}
