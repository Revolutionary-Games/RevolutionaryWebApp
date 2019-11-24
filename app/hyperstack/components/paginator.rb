# frozen_string_literal: true

# Provides pagination in an easy form
class Paginator < HyperComponent
  param :page_size, default: 50, type: Integer
  param :item_count, type: Integer
  param :current_page, type: Integer
  param :on_page_changed, type: Proc
  param :double, default: true, type: Boolean
  param :show_totals, default: false, type: Boolean

  def offset
    @PageSize * @CurrentPage
  end

  def take_count
    @PageSize
  end

  def notify_page(page)
    # This line is needed to make sure this is up to date
    @CurrentPage = page

    @OnPageChanged.call page
  end

  # Needed to make sure that the parent component is always rerendered with paginator set
  after_mount do
    notify_page @CurrentPage
  end

  def pagination_widgets(pages)
    first_page = @CurrentPage.zero?
    last_page = @CurrentPage >= pages

    ReactStrap.Pagination('aria-label' => 'Item page navigation') {
      ReactStrap.PaginationItem(disabled: first_page) {
        ReactStrap.PaginationLink(:first, href: '#').on(:click) { |e|
          e.prevent_default
          notify_page 0
        }
      }
      ReactStrap.PaginationItem(disabled: first_page) {
        ReactStrap.PaginationLink(:previous, href: '#').on(:click) { |e|
          e.prevent_default
          notify_page @CurrentPage - 1
        }
      }

      # TODO: allow limiting the number of page buttons
      (0..pages).each { |i|
        ReactStrap.PaginationItem(active: i == @CurrentPage, key: i) {
          ReactStrap.PaginationLink(href: '#') { (i + 1).to_s }.on(:click) { |e|
            e.prevent_default
            notify_page i
          }
        }
      }

      ReactStrap.PaginationItem(disabled: last_page) {
        ReactStrap.PaginationLink(:next, href: '#').on(:click) { |e|
          e.prevent_default
          notify_page @CurrentPage + 1
        }
      }
      ReactStrap.PaginationItem(disabled: last_page) {
        ReactStrap.PaginationLink(:last, href: '#').on(:click) { |e|
          e.prevent_default
          notify_page pages
        }
      }

      if @ShowTotals
        SPAN(class: 'PaginatorTotals') {
          "Total items: #{@ItemCount} Total pages: #{pages + 1}"
        }
      end
    }
  end

  render(DIV) do
    pages = ((@ItemCount - 1) / @PageSize).to_i

    pages = 0 if pages.negative?

    pagination_widgets pages

    children.each(&:render)

    # TODO: having a back to top button here would be nice
    pagination_widgets pages if @Double
  end
end
