# frozen_string_literal: true

# Provides pagination in an easy form
class Paginator < HyperComponent
  param :page_size, default: 50, type: Integer
  param :item_count, type: Integer
  param :current_page, type: Integer
  param :on_page_changed, type: Proc

  def offset
    @PageSize * @CurrentPage
  end

  def take_count
    @PageSize
  end

  render(DIV) do
    pages = ((@ItemCount - 1) / @PageSize).to_i

    pages = 0 if pages.zero?

    first_page = @CurrentPage.zero?
    last_page = @CurrentPage >= pages

    ReactStrap.Pagination('aria-label' => 'Item page navigation') {
      ReactStrap.PaginationItem(disabled: first_page) {
        ReactStrap.PaginationLink(:first, href: '#').on(:click) { |e|
          e.prevent_default
          @OnPageChanged.call 0
        }
      }
      ReactStrap.PaginationItem(disabled: first_page) {
        ReactStrap.PaginationLink(:previous, href: '#').on(:click) { |e|
          e.prevent_default
          @OnPageChanged.call @CurrentPage - 1
        }
      }

      # TODO: allow limiting the number of page buttons
      (0..pages).each { |i|
        ReactStrap.PaginationItem(active: i == @CurrentPage, key: i) {
          ReactStrap.PaginationLink(href: '#') { (i + 1).to_s }.on(:click) { |e|
            e.prevent_default
            @OnPageChanged.call i
          }
        }
      }

      ReactStrap.PaginationItem(disabled: last_page) {
        ReactStrap.PaginationLink(:next, href: '#').on(:click) { |e|
          e.prevent_default
          @OnPageChanged.call @CurrentPage + 1
        }
      }
      ReactStrap.PaginationItem(disabled: last_page) {
        ReactStrap.PaginationLink(:last, href: '#').on(:click) { |e|
          e.prevent_default
          @OnPageChanged.call pages
        }
      }
    }
  end
end
