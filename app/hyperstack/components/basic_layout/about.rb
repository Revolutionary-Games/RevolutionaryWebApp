# frozen_string_literal: true

# About page describing some info about the app
class About < HyperComponent
  render(DIV) do
    H1 { 'About' }

    P {
      'This is a website for handling Thrive crash information. And other services needed by the Thrive project'
    }

    P {
      'This site uses cookies to keep track of Rails sessions.'
    }
  end
end
