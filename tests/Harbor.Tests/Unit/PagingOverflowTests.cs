using Harbor.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Harbor.Tests.Unit;

public class PagingOverflowTests
{
    [Theory]
    [InlineData(int.MaxValue, 200)]
    [InlineData(21_474_838, 200)]
    [InlineData(int.MaxValue, 2)]
    public void HugePage_IsEmpty_AndPreservesMetadata(int page, int size)
    {
        var response = new DefaultHttpContext().Response;
        var result = new[] { "one", "two" }.ToPage(
            new PageRequest { Page = page, PageSize = size }, response);

        Assert.Empty(result);
        Assert.Equal(page.ToString(), response.Headers[Paging.PageHeader].ToString());
        Assert.Equal("2", response.Headers[Paging.TotalCountHeader].ToString());
    }
}
