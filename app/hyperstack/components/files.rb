# frozen_string_literal: true

# Shows stored files
class Files < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    H2 { 'Files' }
    P {
      "This is a service to store files needed for Thrive development, which aren't "\
      'included in the code repository.'
    }
  end
end
