# frozen_string_literal: true

# Overall site status
class SiteStatus < HyperComponent
  render(DIV) do
    P { 'TODO: make a serverop that checks that are environment variables are fine' }

    P {
      SPAN { 'You can check background job statuses ' }
      A(href: '/admin/sidekiq', target: '_blank') { 'here' }
      SPAN { '.' }
    }
  end
end
