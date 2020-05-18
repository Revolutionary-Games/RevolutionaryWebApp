# frozen_string_literal: true

require 'aws-sdk-s3'

module API
  module V1
    # LFS API endpoint
    class LFS < Grape::API
      include API::V1::Defaults

      content_type :json, 'application/vnd.git-lfs+json'

      LFS_UPLOAD_KEY_DERIVE = 'lfs_upload'

      helpers do
        def request_auth
          error!('Unauthorized', 401,
                 'LFS-Authenticate' => 'Basic realm="ThriveDevCenter Git LFS"')
        end

        def check_server_config
          if !ENV['LFS_STORAGE_DOWNLOAD'] || !ENV['LFS_STORAGE_DOWNLOAD_KEY'] ||
             !ENV['LFS_STORAGE_S3_ENDPOINT'] || !ENV['LFS_STORAGE_S3_BUCKET'] ||
             !ENV['LFS_STORAGE_S3_REGION'] || !ENV['LFS_STORAGE_S3_ACCESS_KEY'] ||
             !ENV['LFS_STORAGE_S3_SECRET_KEY'] || !ENV['BASE_URL']
            Rails.logger.error 'Server configuration variables is invalid'
            error!('Invalid Server Configuration', 500)
          end
        end

        def check_auth
          if headers['Authorization']
            user = nil

            begin
              decoded = Base64.decode64(headers['Authorization'].split(' ', 2).second || '')

              username, token = decoded.split(':', 2)

              user = User.find_by email: username, lfs_token: token
            rescue StandardError
              # Invalid format for Authorization header
              request_auth
            end

            request_auth unless user

            return user
          end
          nil
        end

        def read_operation
          error!({ error_code: 400, message: 'Missing operation' }, 400) unless
            params[:operation]

          case params[:operation]
          when 'download'
            return :download
          when 'upload'
            return :upload
          else
            error!({ error_code: 400, message: 'Invalid operation' }, 400)
          end
        end

        def read_adapter
          if params[:transfers]
            if !params[:transfers].is_a?(Array) || !params[:transfers].include?('basic')
              error!({ error_code: 400, message:
                                          'Only basic transfer adapter is supported' }, 400)
            end
          end

          :basic
        end

        def check_common_lfs_options
          if !params[:objects] || !params[:objects].is_a?(Array)
            error!({ error_code: 400, message: 'Missing objects array' }, 400)
          end

          [read_operation, read_adapter]
        end

        def handle_download(project, obj)
          object = LfsObject.find_by lfs_project_id: project.id, oid: obj[:oid]

          unless object
            return [{}, {
              code: 404,
              message: 'OID not found'
            }]
          end

          url, expires_in = LfsHelper.create_download_for_lfs_object object

          [
            {
              download:
                {
                  "href": url,
                  expires_in: expires_in
                }
            }, nil
          ]
        end

        def oid_storage_path(project, oid)
          "#{project.slug}/objs/#{oid[0..1]}/#{oid[2..3]}/#{oid[4..]}"
        end

        def upload_derived_key
          Rails.application.key_generator.generate_key(LFS_UPLOAD_KEY_DERIVE)
        end

        def upload_verify_token(oid, size)
          payload = { object_oid: oid, object_size: size,
                      # Some leeway is added on top of what we tell
                      # the client the expiry time is
                      exp: Time.now.to_i + LfsHelper::UPLOAD_EXPIRE_TIME + 10 }

          JWT.encode payload, upload_derived_key, 'HS256'
        end

        def upload_target_bucket
          aws_client = Aws::S3::Client.new(
            region: ENV['LFS_STORAGE_S3_REGION'],
            endpoint: ENV['LFS_STORAGE_S3_ENDPOINT'],
            access_key_id: ENV['LFS_STORAGE_S3_ACCESS_KEY'],
            secret_access_key: ENV['LFS_STORAGE_S3_SECRET_KEY']
          )

          s3 = Aws::S3::Resource.new(client: aws_client)

          s3.bucket ENV['LFS_STORAGE_S3_BUCKET']
        end

        # This doesn't yet create the LfsObject to protect against failed uploads
        def handle_upload(project, obj, user)
          object = LfsObject.find_by lfs_project_id: project.id, oid: obj[:oid]

          if object
            # We already have this object
            return [{}, nil]
          end

          # New object. User must have write access
          unless user&.developer?
            Rails.logger.info 'Requesting auth because new object is to be ' \
                              "uploaded: #{obj[:oid]} for project #{project.name}"
            request_auth
            # This return is here to just be extra safe
            return [{}, {
              code: 403,
              message: 'Write access needed'
            }]
          end

          oid = obj[:oid]
          size = obj[:size]

          storage_path = oid_storage_path project, oid

          begin
            bucket = upload_target_bucket

            # One extra call to AWS to make sure the target bucket is
            # valid to avoid configuration issues. Can be removed if
            # this is a performance issue
            unless bucket.exists?
              raise 'Target S3 bucket does not exist or configured credentials are wrong'
            end

            s3_obj = bucket.object(storage_path)

            # This probably doesn't connect to amazon at all, so we don't know if our
            # token is invalid here. So this step likely cannot fail
            url = s3_obj.presigned_url(:put, expires_in: LfsHelper::UPLOAD_EXPIRE_TIME + 1)

            [
              {
                upload:
                  {
                    "href": url,
                    expires_in: LfsHelper::UPLOAD_EXPIRE_TIME
                  },
                verify:
                  {
                    # Complex URL build to not have to detect v1 and /api paths
                    "href": URI.join(ENV['BASE_URL'],
                                     URI(request.url).request_uri,
                                     "../verify?token=#{upload_verify_token oid, size}").to_s,
                    expires_in: LfsHelper::UPLOAD_EXPIRE_TIME + 1
                  }
              }, nil
            ]
          rescue RuntimeError => e
            return [{}, {
              code: 500,
              message: "Internal server error: #{e}"
            }]
          end
        end

        def build_result_obj(obj, error, actions)
          result = {
            oid: obj[:oid],
            size: obj[:size],
            authenticated: true
          }

          if error
            result[:error] = error
          elsif !actions.empty?
            raise 'Actions must be a hash' unless actions.is_a?(Hash)

            result[:actions] = actions
          end

          result
        end

        def handle_lfs_object_request(project, obj, operation, user)
          error = nil
          actions = {}

          if operation == :download

            actions, error = handle_download project, obj

          elsif operation == :upload

            actions, error = handle_upload project, obj, user
          else
            error = {
              code: 500,
              message: 'Server operation logic error'
            }
          end

          build_result_obj obj, error, actions
        end
      end

      resource :lfs do
        desc 'Git LFS endpoints'
        get '' do
          { error: 'no project specified' }
        end

        desc 'Basic project info'
        params do
          optional :lfs_token, type: String
          requires :slug, type: String, desc: 'LFS project slug'
        end
        get ':slug' do
          user = nil

          if permitted_params[:lfs_token]
            user = User.find_by lfs_token: permitted_params[:lfs_token]
            error!({ error_code: 403, error: 'Invalid LFS token' }, 403) unless user
          end

          project = LfsProject.find_by slug: permitted_params[:slug]

          if !project || (!project.public? && !user&.developer?)

            error!({ error_code: 404, error: 'No project found' }, 404)
            return
          end

          { id: project.id, slug: project.slug, name: project.name }
        end

        desc 'Git LFS batch API'
        params do
          requires :slug, type: String, desc: 'LFS project slug'
        end
        post ':slug/objects/batch' do
          user = check_auth

          content_type 'application/vnd.git-lfs+json'

          project = LfsProject.find_by slug: permitted_params[:slug]

          request_auth if !project&.public? && !user&.developer?

          error!({ error_code: 404, message: 'No project found' }, 404) unless project

          operation, adapter = check_common_lfs_options

          check_server_config

          processed_objects = []

          params[:objects].each { |obj|
            if !obj.include?(:oid) || !obj.include?(:size) || !obj[:size].is_a?(Integer) ||
               obj[:size].negative? || obj[:oid].length < 5
              processed_objects.push(
                Hash.new(
                  oid: obj[:oid],
                  size: obj[:size],
                  error: {
                    code: 422,
                    message: 'OID or size missing (or invalid)'
                  }
                )
              )
              next
            end

            # If this is an upload of a new file this checks that user has access
            processed_objects.push handle_lfs_object_request(project, obj, operation, user)
          }

          if processed_objects.empty?
            error!({ error_code: 422, message: 'No valid request object' }, 422)
          end

          Rails.logger.info 'LFS batch request succeeded'

          status 200
          {
            transfer: adapter.to_s,
            objects: processed_objects
          }
        end

        desc 'Verify that uploaded object is fine and then create the server side object'
        params do
          requires :slug, type: String, desc: 'LFS project slug'
          requires :token, type: String, desc: 'LFS upload verify token'
          requires :oid, type: String, desc: 'OID of the uploaded object'
          requires :size, type: Integer, desc: 'size of the object'
        end
        post ':slug/verify' do
          content_type 'application/vnd.git-lfs+json'

          # Verify token first as there is no other protection on this endpoint
          begin
            decoded_token = JWT.decode permitted_params[:token],
                                       upload_derived_key, true, algorithm: 'HS256'
            data = decoded_token[0]
          rescue JWT::DecodeError
            error!({ error_code: 403, message: 'Invalid token' }, 403)
          end

          if data['object_oid'] != permitted_params[:oid] ||
             data['object_size'] != permitted_params[:size]
            error!({ error_code: 403, message: 'Invalid token' }, 403)
          end

          project = LfsProject.find_by slug: permitted_params[:slug]

          error!({ error_code: 404, message: 'No project found' }, 404) unless project

          object = LfsObject.find_by lfs_project_id: project.id, oid: data['object_oid']

          # Error if we already have this object
          error!({ error_code: 400, message: 'Object already exists' }, 400) if object

          # Verify that the S3 upload succeeded
          storage_path = oid_storage_path project, data['object_oid']

          begin
            bucket = upload_target_bucket

            s3_obj = bucket.object(storage_path)

            existing_length = s3_obj.content_length
          rescue RuntimeError => e
            error!({ error_code: 400,
                     message: "Object could not be verified to exist in storage: #{e}" }, 400)
          end

          if existing_length != data['object_size']
            error!({ error_code: 400,
                     message: "Uploaded object size doesn't match expected " \
                     "value: #{existing_length} != #{data['object_size']} (expected)" }, 400)
          end

          # Now the object is good in a verified way, we can create it
          lfs_object = LfsObject.create oid: data['object_oid'], storage_path: storage_path,
                                        lfs_project: project, size: data['object_size']

          unless lfs_object.valid?
            error!({ error_code: 400,
                     message: 'Object cannot be saved. Validation failures: ' +
                     lfs_object.errors.full_messages.join('.\n') }, 400)
          end

          unless lfs_object.save
            error!({ error_code: 500,
                     message: 'Object cannot be saved. Database write failed.' }, 500)
          end

          Rails.logger.info "New LFS object uploaded: #{lfs_object.oid} " \
                            "for project #{project.name}"

          status 200
          {
            message: 'Object successfully created'
          }
        end

        desc 'UNIMPLEMENTED Git LFS locks API'
        post ':slug/locks/verify' do
          content_type 'application/vnd.git-lfs+json'
          status 501
          { message: 'Not implemented' }
        end
      end
    end
  end
end
