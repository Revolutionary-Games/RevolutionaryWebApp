# frozen_string_literal: true

class AccessKeyItem < HyperComponent
  param :accessKey

  before_mount do
    @delete_in_progress = false
    @delete_pressed = false
    @delete_error = nil
  end

  render(TR) do
    return if @AccessKey.nil?

    TD { @AccessKey.id.to_s }
    TD {      @AccessKey.description.to_s    }
    TD {      @AccessKey.last_used ? @AccessKey.last_used.to_s : 'never'    }
    TD {      @AccessKey.key_type_pretty    }
    TD {
      P{ @delete_error.to_s } if @delete_error
      RS.Button(color: 'danger', disabled: @delete_pressed) {
        SPAN{'Delete'}
        RS.Spinner(size: 'sm') if @delete_in_progress
      } .on(:click) {
        mutate {
          @delete_pressed = true
          @delete_in_progress = true
          @delete_error = nil
        }

        DeleteAccessKey.run(key_id: @AccessKey.id).then{
          mutate {
            @delete_in_progress = false
          }
        }.fail { |error|
          mutate {
            @delete_in_progress = false
            @delete_pressed = false
            @delete_error = "Error: #{error}"
          }
        }
      }
    }
  end
end

# Access key management
class AccessKeys < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @creating_new_key = false
    @new_key_type = 'devbuilds'
    @new_key_description = ''

    @create_in_progress = false
    @create_status = nil
  end

  render(DIV) do
    RS.Table(:striped, :responsive) {
      THEAD {
        TR {
          TH { 'ID' }
          TH { 'Description' }
          TH { 'Last Accessed' }
          TH { 'Scope' }
          TH { 'Actions' }
        }
      }

      TBODY {
        AccessKey.each { |key|
          AccessKeyItem(access_key: key, key: key&.id)
        }
      }
    }

    RS.Button(color: 'success') {
      'New Key'
    } .on(:click) {
      mutate {
        @creating_new_key = !@creating_new_key
        @new_key_description = ''
      }
    }

    BR {}

    RS.Spinner(color: 'primary') if @create_in_progress
    P { @create_status.to_s } if @create_status

    if @creating_new_key
      RS.Form {
        RS.FormGroup {
          RS.Label { 'Description for key' }
          RS.Input(type: :text, placeholder: 'description',
                   value: @new_key_description).on(:change) { |e|
            mutate {
              @new_key_description = e.target.value
            }
          }
        }

        RS.FormGroup {
          RS.Label { 'Type' }
          RS.Input(type: :select, placeholder: 'Name', value: @new_key_type) {
            OPTION { 'devbuilds' }
          }.on(:change) { |e|
            mutate @new_key_type = e.target.value
          }
        }

        RS.Button(type: 'submit', color: 'primary', disabled: @new_key_description.blank?) {
          'Create'
        }.on(:click) { |event|
          event.prevent_default
          mutate {
            @create_in_progress = true
            @create_status = nil
          }

          CreateAccessKey.run(description: @new_key_description,
                              key_type: @new_key_type).then { |key|
            mutate {
              @creating_new_key = false
              @create_in_progress = false
              @create_status = "Key code is: #{key} COPY THIS NOW! This is "\
                               'the last time you see it'
            }
          }.fail { |error|
            mutate {
              @create_in_progress = false
              @create_status = "Error: #{error}"
            }
          }
        }
        RS.Button(color: 'secondary', class: 'LeftMargin') {
          'Cancel'
        }.on(:click) { |event|
          event.prevent_default
          mutate @creating_new_key = false
        }
      }
    end
  end
end
